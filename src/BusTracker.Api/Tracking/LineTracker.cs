using BusTracker.Api.Configuration;
using BusTracker.Api.Ibb;
using BusTracker.Api.Shapes;

namespace BusTracker.Api.Tracking;

public static class BusStatus
{
    public const string Ok = "ok";
    public const string AtTerminal = "at-terminal";
    public const string Arrived = "arrived";
    public const string Stale = "stale";
    public const string OffRoute = "off-route";
    public const string NoRoute = "no-route";
}

public sealed record TrackedBus(
    LiveBus Bus,
    string TripId,
    bool OnOwnVariant,
    double? AlongMeters,
    double? OffsetMeters,
    double RouteLengthMeters,
    PlacedStop? LastStop,
    PlacedStop? NextStop,
    double? SpeedMps,
    int AgeSeconds,
    string Status,
    IReadOnlyList<PlacedStop> PassedSinceLastUpdate)
{
    public bool IsUsable => Status is BusStatus.Ok or BusStatus.AtTerminal && AlongMeters is not null;
}

public sealed class LineTracker(TrackingOptions options)
{
    private static readonly TimeSpan SpeedWindow = TimeSpan.FromMinutes(6);
    private static readonly TimeSpan ForgetAfter = TimeSpan.FromMinutes(30);
    private const double MinSpeedSampleSeconds = 50;
    private const double NewTripBackwardsMeters = 500;
    private const double MaxBusSpeedMps = 30;
    private const double WindowToleranceMeters = 50;
    private const double StraightLineOffRouteMeters = 750;

    private readonly Dictionary<string, BusState> _states = [];

    public IReadOnlyList<TrackedBus> Update(IReadOnlyList<LiveBus> buses, LineTracks tracks, DateTimeOffset now)
    {
        var tracked = buses.Select(bus => Track(bus, tracks, now)).ToList();
        Forget(now);
        return tracked;
    }

    private TrackedBus Track(LiveBus bus, LineTracks tracks, DateTimeOffset now)
    {
        var ageSeconds = (int)Math.Max(0, (now - bus.ReportedAt).TotalSeconds);
        var track = tracks.For(bus.RouteCode, bus.Direction);
        if (track is null) return Untracked(bus, ageSeconds);

        _states.TryGetValue(bus.DoorNo, out var previous);
        var projection = Project(bus, track, previous);
        var state = Advance(bus, track, projection, previous);
        _states[bus.DoorNo] = state.Current;

        return new TrackedBus(
            bus,
            state.Current.TripId,
            OnOwnVariant: track.RouteCode == bus.RouteCode,
            projection.AlongMeters,
            projection.OffsetMeters,
            track.Geometry.LengthMeters,
            track.LastStopReached(projection.AlongMeters),
            track.NextStop(projection.AlongMeters),
            state.Current.SpeedMps(),
            ageSeconds,
            Classify(track, projection, ageSeconds),
            state.Passed);
    }

    private static Projection Project(LiveBus bus, RouteTrack track, BusState? previous)
    {
        var point = new LatLng(bus.Lat, bus.Lng);
        var anywhere = track.Geometry.Project(point);
        if (previous is null || previous.RouteCode != bus.RouteCode) return anywhere;

        var seconds = Math.Max(0, (bus.ReportedAt - previous.ReportedAt).TotalSeconds);
        var nearPrevious = track.Geometry.Project(point,
            minAlong: previous.AlongMeters - NewTripBackwardsMeters,
            maxAlong: previous.AlongMeters + seconds * MaxBusSpeedMps + 300);
        return nearPrevious.OffsetMeters <= anywhere.OffsetMeters + WindowToleranceMeters ? nearPrevious : anywhere;
    }

    private static (BusState Current, IReadOnlyList<PlacedStop> Passed) Advance(
        LiveBus bus, RouteTrack track, Projection projection, BusState? previous)
    {
        if (previous is not null && bus.ReportedAt <= previous.ReportedAt) return (previous, []);

        if (previous is null || IsNewTrip(bus, projection, previous))
            return (BusState.Start(bus, projection.AlongMeters), []);

        var passed = track.StopsPassedBetween(previous.AlongMeters, projection.AlongMeters).ToList();
        return (previous.MoveTo(bus.ReportedAt, projection.AlongMeters, SpeedWindow), passed);
    }

    private static bool IsNewTrip(LiveBus bus, Projection projection, BusState previous) =>
        previous.RouteCode != bus.RouteCode || projection.AlongMeters < previous.AlongMeters - NewTripBackwardsMeters;

    private string Classify(RouteTrack track, Projection projection, int ageSeconds)
    {
        if (ageSeconds > options.StaleAfterSeconds) return BusStatus.Stale;
        if (projection.OffsetMeters > OffRouteLimit(track)) return BusStatus.OffRoute;
        if (IsAtStart(track, projection.AlongMeters)) return BusStatus.AtTerminal;
        if (IsAtEnd(track, projection.AlongMeters)) return BusStatus.Arrived;
        return BusStatus.Ok;
    }

    private double OffRouteLimit(RouteTrack track) =>
        track.HasRoadGeometry ? options.OffRouteMeters : Math.Max(options.OffRouteMeters, StraightLineOffRouteMeters);

    private bool IsAtStart(RouteTrack track, double along) =>
        along <= (track.Stops.FirstOrDefault()?.AlongMeters ?? 0) + options.TerminalMeters;

    private bool IsAtEnd(RouteTrack track, double along) =>
        along >= (track.Stops.LastOrDefault()?.AlongMeters ?? track.Geometry.LengthMeters) - options.TerminalMeters;

    private static TrackedBus Untracked(LiveBus bus, int ageSeconds) =>
        new(bus, "", false, null, null, 0, null, null, null, ageSeconds, BusStatus.NoRoute, []);

    private void Forget(DateTimeOffset now)
    {
        foreach (var doorNo in _states.Where(s => now - s.Value.ReportedAt > ForgetAfter).Select(s => s.Key).ToList())
            _states.Remove(doorNo);
    }

    private sealed record BusState(
        string RouteCode,
        string TripId,
        DateTimeOffset ReportedAt,
        double AlongMeters,
        IReadOnlyList<(DateTimeOffset At, double Along)> Samples)
    {
        public static BusState Start(LiveBus bus, double along) =>
            new(bus.RouteCode, $"{bus.DoorNo}:{bus.RouteCode}:{bus.ReportedAt:yyyyMMddHHmm}", bus.ReportedAt, along,
                [(bus.ReportedAt, along)]);

        public BusState MoveTo(DateTimeOffset at, double along, TimeSpan window) =>
            this with
            {
                ReportedAt = at,
                AlongMeters = along,
                Samples = [.. Samples.Where(s => at - s.At <= window), (at, along)],
            };

        public double? SpeedMps()
        {
            var seconds = (Samples[^1].At - Samples[0].At).TotalSeconds;
            if (seconds < MinSpeedSampleSeconds) return null;
            return Math.Max(0, (Samples[^1].Along - Samples[0].Along) / seconds);
        }
    }
}
