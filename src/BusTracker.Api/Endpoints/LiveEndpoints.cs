using BusTracker.Api.Ibb;
using BusTracker.Api.Lines;
using BusTracker.Api.Timetables;
using BusTracker.Api.Tracing;
using BusTracker.Api.Tracking;

namespace BusTracker.Api.Endpoints;

public static class LiveEndpoints
{
    public static IEndpointRouteBuilder MapLiveEndpoints(this IEndpointRouteBuilder app)
    {
        var lines = app.MapGroup("/api/lines/{code}");
        lines.MapGet("/live", GetLive);
        lines.MapGet("/arrivals", GetArrivals);
        lines.MapGet("/path", GetBusPath);
        return app;
    }

    private static async Task<IResult> GetLive(string code, LineDataService data, LinePlans plans, TimeProvider clock, CancellationToken ct)
    {
        if (LineCode.Normalize(code) is not { } line) return LineCode.Invalid();
        if (await LineCode.LoadStopsAsync(line, data, ct) is null) return LineCode.Unknown(line);

        var tracks = await data.GetTracksAsync(line, ct);
        var plan = await plans.GetAsync(line, ct);
        return await WithLiveAsync(line, data, clock, ct, live =>
        {
            var now = clock.GetUtcNow();
            var planned = plan is null ? [] : tracks.Mains
                .Select(t => plan.For(t.Direction, null, WaitingIn(live.Snapshot.Buses, t.Direction), now))
                .ToList();
            var waiting = planned.SelectMany(p => p.Waiting).ToDictionary();
            var buses = live.Snapshot.Buses
                .Select(b => ToView(b, HeavyTrafficAhead(b, tracks, data.Hops, now)) with { Planned = waiting.GetValueOrDefault(b.Bus.DoorNo) })
                .ToList();
            return Results.Ok(new LineLiveResponse(line, Freshness.From(live, clock), buses,
                planned.Select(p => p.View).OfType<PlannedView>().ToList(), plan?.Announcements ?? []));
        });
    }

    private static IReadOnlyList<WaitingBus> WaitingIn(IEnumerable<TrackedBus> buses, string direction) =>
        buses.Where(b => b.Bus.Direction == direction && b.Status == BusStatus.AtTerminal)
            .Select(b => new WaitingBus(b.Bus.DoorNo, b.Bus.RouteCode))
            .ToList();

    private static async Task<IResult> GetArrivals(
        string code, string stop, string dir, LineDataService data, LinePlans plans, TimeProvider clock, CancellationToken ct)
    {
        if (LineCode.Normalize(code) is not { } line) return LineCode.Invalid();
        if (await LineCode.LoadStopsAsync(line, data, ct) is null) return LineCode.Unknown(line);

        var tracks = await data.GetTracksAsync(line, ct);
        var direction = dir.Trim().ToUpperInvariant();
        if (tracks.Main(direction)?.FindStop(stop) is not { } target)
            return Results.Problem($"Stop {stop} is not on line {line} direction {direction}.", statusCode: StatusCodes.Status404NotFound);

        var plan = await plans.GetAsync(line, ct);
        return await WithLiveAsync(line, data, clock, ct, live =>
        {
            var now = clock.GetUtcNow();
            var arrivals = Arrivals.ForStop(stop, direction, live.Snapshot.Buses, tracks, data.Eta(now));
            var planned = plan?.For(direction, target.StopCode, LinePlans.WaitingAmong(arrivals.Approaching), now);
            var approaching = arrivals.Approaching
                .Select(a => ToArrivalView(a) with { Planned = planned?.Waiting.GetValueOrDefault(a.Bus.Bus.DoorNo) })
                .ToList();
            Telemetry.Note("etas", EtaNote.Of(approaching.Select(a => (line, a))));
            return Results.Ok(new StopArrivalsResponse(
                line, direction, new StopRef(target.StopCode, target.Seq, target.Name), Freshness.From(live, clock),
                approaching,
                arrivals.RecentlyPassed.Select(ToView).ToList(),
                planned?.View, plan?.Announcements ?? []));
        });
    }

    private static async Task<IResult> GetBusPath(
        string code, string bus, string stop, LineDataService data, TimeProvider clock, CancellationToken ct)
    {
        if (LineCode.Normalize(code) is not { } line) return LineCode.Invalid();
        if (await LineCode.LoadStopsAsync(line, data, ct) is null) return LineCode.Unknown(line);

        var tracks = await data.GetTracksAsync(line, ct);
        return await WithLiveAsync(line, data, clock, ct, live =>
        {
            var (path, problem) = BusPaths.Between(bus.Trim(), stop.Trim(), live.Snapshot.Buses, tracks);
            return problem switch
            {
                BusPathProblem.BusNotFound => Results.Problem($"Bus {bus} isn't on line {line} right now.", statusCode: StatusCodes.Status404NotFound),
                BusPathProblem.StopNotOnRoute => Results.Problem($"This bus's route doesn't stop at {stop}.", statusCode: StatusCodes.Status404NotFound),
                _ => Results.Ok(ToView(line, path!, Freshness.From(live, clock))),
            };
        });
    }

    private static BusPathView ToView(string line, BusPath path, Freshness data) =>
        new(line, path.Track.RouteCode, path.Track.Variant, path.Track.HasRoadGeometry,
            ToView(path.Bus),
            new PathStopView(path.Stop.Seq, path.Stop.StopCode, path.Stop.Name, path.Stop.Lat, path.Stop.Lng),
            path.Passed, Math.Round(path.DistanceMeters),
            path.Points.Select(p => new[] { p.Lat, p.Lng }).ToList(),
            path.RemainingStops.Select(s => new PathStopView(s.Seq, s.StopCode, s.Name, s.Lat, s.Lng)).ToList(),
            data);

    private static async Task<IResult> WithLiveAsync(
        string line, LineDataService data, TimeProvider clock, CancellationToken ct, Func<LiveResult, IResult> respond)
    {
        try
        {
            return respond(await data.GetLiveAsync(line, ct));
        }
        catch (LineUnavailableException e)
        {
            return LineCode.Unavailable(e, clock);
        }
        catch (IbbFaultException)
        {
            var empty = new LiveResult(new LiveSnapshot([], clock.GetUtcNow()), false, null, null);
            return respond(empty);
        }
        catch (Exception e) when (e is IbbException or HttpRequestException || e is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return LineCode.Unavailable(new LineUnavailableException("ibb-error", clock.GetUtcNow().AddMinutes(1)), clock);
        }
    }

    private static bool HeavyTrafficAhead(TrackedBus bus, LineTracks tracks, HopTimes hops, DateTimeOffset now) =>
        bus is { IsUsable: true, AlongMeters: { } along }
        && tracks.For(bus.Bus.RouteCode, bus.Bus.Direction) is { } track
        && TravelTime.HeavyTrafficAhead(track, along, bus.SpeedMps, hops, now);

    private static TrackedBusView ToView(TrackedBus t) => ToView(t, false);

    private static TrackedBusView ToView(TrackedBus t, bool heavyTraffic) =>
        new(t.Bus.DoorNo, t.Bus.Lat, t.Bus.Lng, t.Bus.Direction, t.Bus.Variant, t.Bus.RouteCode, t.Bus.Destination,
            t.Bus.ReportedAt, t.AgeSeconds, t.Status, t.TripId, t.OnOwnVariant,
            Round(t.AlongMeters), Round(t.OffsetMeters), Round(t.RouteLengthMeters),
            t.LastStop?.Seq, t.NextStop?.Seq, Progress(t),
            t.SpeedMps is { } mps ? Math.Round(mps * 3.6, 1) : null,
            t.PassedSinceLastUpdate.Select(s => s.Seq).ToList(), heavyTraffic);

    internal static ArrivalView ToArrivalView(Arrival a) =>
        new(a.Bus.Bus.DoorNo, a.Bus.Bus.RouteCode, a.Bus.Bus.Variant, a.StopsAway, Math.Round(a.DistanceMeters),
            a.EtaSeconds, a.NotDeparted, a.Bus.AgeSeconds, a.Bus.LastStop?.Seq, a.Bus.LastStop?.Name, a.HeavyTraffic);

    private static PassedView ToView(PassedBus p) =>
        new(p.Bus.Bus.DoorNo, p.Bus.Bus.RouteCode, p.Bus.Bus.Variant, Math.Round(p.MetersPast), p.Bus.LastStop?.Seq);

    private static double? Progress(TrackedBus t)
    {
        if (t.AlongMeters is not { } along || t.LastStop is null || t.NextStop is null) return null;
        var span = t.NextStop.AlongMeters - t.LastStop.AlongMeters;
        return span <= 0 ? null : Math.Round(Math.Clamp((along - t.LastStop.AlongMeters) / span, 0, 1), 2);
    }

    private static double? Round(double? value) => value is { } v ? Math.Round(v) : null;
}

public sealed record Freshness(DateTimeOffset FetchedAt, int AgeSeconds, bool Stale, string? StaleReason, DateTimeOffset? RetryAt)
{
    public static Freshness From(LiveResult live, TimeProvider clock) =>
        new(live.Snapshot.FetchedAt, (int)(clock.GetUtcNow() - live.Snapshot.FetchedAt).TotalSeconds,
            live.Stale, live.StaleReason, live.RetryAt);
}

public sealed record TrackedBusView(
    string DoorNo, double Lat, double Lng, string Direction, string Variant, string RouteCode, string Destination,
    DateTimeOffset ReportedAt, int AgeSeconds, string Status, string TripId, bool OnOwnVariant,
    double? AlongMeters, double? OffsetMeters, double? RouteLengthMeters,
    int? LastStopSeq, int? NextStopSeq, double? ProgressToNext, double? SpeedKmh, IReadOnlyList<int> PassedStopSeqs, bool HeavyTraffic,
    PlannedTripView? Planned = null);

public sealed record LineLiveResponse(
    string Line, Freshness Data, IReadOnlyList<TrackedBusView> Buses,
    IReadOnlyList<PlannedView> Planned, IReadOnlyList<AnnouncementView> Announcements);

public sealed record StopRef(string Code, int Seq, string Name);

public sealed record ArrivalView(
    string DoorNo, string RouteCode, string Variant, int StopsAway, double DistanceMeters, int? EtaSeconds,
    bool NotDeparted, int AgeSeconds, int? LastStopSeq, string? LastStopName, bool HeavyTraffic,
    PlannedTripView? Planned = null);

public sealed record PassedView(string DoorNo, string RouteCode, string Variant, double MetersPast, int? LastStopSeq);

public sealed record StopArrivalsResponse(
    string Line, string Direction, StopRef Stop, Freshness Data,
    IReadOnlyList<ArrivalView> Approaching, IReadOnlyList<PassedView> RecentlyPassed,
    PlannedView? Planned, IReadOnlyList<AnnouncementView> Announcements);

public sealed record PathStopView(int Seq, string Code, string Name, double Lat, double Lng);

public sealed record BusPathView(
    string Line, string RouteCode, string Variant, bool OnRoadGeometry, TrackedBusView Bus, PathStopView Stop,
    bool Passed, double DistanceMeters, IReadOnlyList<double[]> Path, IReadOnlyList<PathStopView> RemainingStops, Freshness Data);
