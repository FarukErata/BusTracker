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
    private const int FirstMeasuredHop = 1;
    private const double MinHopSeconds = 3;
    private const double MaxHopSeconds = 30 * 60;
    private const double MinHopSpeedMps = 0.2;

    private readonly Dictionary<string, BusState> _states = [];

    public IReadOnlyList<TrackedBus> Update(
        IReadOnlyList<LiveBus> buses, LineTracks tracks, DateTimeOffset now, TrackerEvents? events = null)
    {
        var tracked = buses.Select(bus => Track(bus, tracks, now, events)).ToList();
        Forget(now);
        return tracked;
    }

    public void Reset(string doorNo) => _states.Remove(doorNo);

    private TrackedBus Track(LiveBus bus, LineTracks tracks, DateTimeOffset now, TrackerEvents? events)
    {
        var ageSeconds = (int)Math.Max(0, (now - bus.ReportedAt).TotalSeconds);
        var track = tracks.For(bus.RouteCode, bus.Direction);
        if (track is null) return Untracked(bus, ageSeconds);

        _states.TryGetValue(bus.DoorNo, out var previous);
        var projection = Project(bus, track, previous);
        var state = Advance(bus, track, projection, previous, events);
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
        LiveBus bus, RouteTrack track, Projection projection, BusState? previous, TrackerEvents? events)
    {
        if (previous is not null && bus.ReportedAt <= previous.ReportedAt) return (previous, []);

        if (previous is null || IsNewTrip(bus, projection, previous))
            return (BusState.Start(bus, projection.AlongMeters), []);

        var passed = track.StopsPassedBetween(previous.AlongMeters, projection.AlongMeters).OrderBy(s => s.Index).ToList();
        var moved = previous.MoveTo(bus.ReportedAt, projection.AlongMeters, SpeedWindow);
        foreach (var stop in passed)
            moved = Cross(moved, previous, bus, projection.AlongMeters, track, stop, events);
        return (moved, passed);
    }

    private static BusState Cross(
        BusState state, BusState previous, LiveBus bus, double along, RouteTrack track, PlacedStop stop, TrackerEvents? events)
    {
        var fraction = (stop.AlongMeters - previous.AlongMeters) / (along - previous.AlongMeters);
        var crossedAt = previous.ReportedAt + (bus.ReportedAt - previous.ReportedAt) * Math.Clamp(fraction, 0, 1);
        events?.Crossed?.Invoke(new StopCrossing(track.RouteCode, bus.DoorNo, stop.Index, crossedAt));
        if (state.CrossedStop == stop.Index - 1 && stop.Index - 1 >= FirstMeasuredHop && state.CrossedAt is { } since)
        {
            var seconds = (crossedAt - since).TotalSeconds;
            var meters = stop.AlongMeters - track.Stops[stop.Index - 1].AlongMeters;
            if (IsPlausibleHop(meters, seconds))
                events?.Hop?.Invoke(new HopObservation(track.RouteCode, stop.Index - 1, crossedAt, seconds));
        }
        return state with { CrossedStop = stop.Index, CrossedAt = crossedAt };
    }

    private static bool IsPlausibleHop(double meters, double seconds) =>
        meters > 0 && seconds >= MinHopSeconds && seconds <= MaxHopSeconds
        && meters / seconds <= MaxBusSpeedMps && meters / seconds >= MinHopSpeedMps;

    private static bool IsNewTrip(LiveBus bus, Projection projection, BusState previous) =>
        previous.RouteCode != bus.RouteCode || projection.AlongMeters < previous.AlongMeters - NewTripBackwardsMeters;

    private string Classify(RouteTrack track, Projection projection, int ageSeconds)
    {
        if (ageSeconds > options.StaleAfterSeconds) return BusStatus.Stale;
        if (projection.OffsetMeters > OffRouteLimit(options, track)) return BusStatus.OffRoute;
        if (IsAtStart(track, projection.AlongMeters)) return BusStatus.AtTerminal;
        if (IsAtEnd(track, projection.AlongMeters)) return BusStatus.Arrived;
        return BusStatus.Ok;
    }

    public static double OffRouteLimit(TrackingOptions options, RouteTrack track) =>
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
        IReadOnlyList<(DateTimeOffset At, double Along)> Samples,
        int? CrossedStop = null,
        DateTimeOffset? CrossedAt = null)
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
