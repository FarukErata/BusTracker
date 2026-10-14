using BusTracker.Api.Ibb;
using BusTracker.Api.Lines;
using BusTracker.Api.Tracking;

namespace BusTracker.Api.Endpoints;

public static class LiveEndpoints
{
    public static IEndpointRouteBuilder MapLiveEndpoints(this IEndpointRouteBuilder app)
    {
        var lines = app.MapGroup("/api/lines/{code}");
        lines.MapGet("/live", GetLive);
        lines.MapGet("/arrivals", GetArrivals);
        return app;
    }

    private static async Task<IResult> GetLive(string code, LineDataService data, TimeProvider clock, CancellationToken ct)
    {
        if (LineCode.Normalize(code) is not { } line) return LineCode.Invalid();
        if (await LineCode.LoadStopsAsync(line, data, ct) is null) return LineCode.Unknown(line);

        return await WithLiveAsync(line, data, clock, ct, live =>
            Results.Ok(new LineLiveResponse(line, Freshness.From(live, clock), live.Snapshot.Buses.Select(ToView).ToList())));
    }

    private static async Task<IResult> GetArrivals(
        string code, string stop, string dir, LineDataService data, TimeProvider clock, CancellationToken ct)
    {
        if (LineCode.Normalize(code) is not { } line) return LineCode.Invalid();
        if (await LineCode.LoadStopsAsync(line, data, ct) is null) return LineCode.Unknown(line);

        var tracks = await data.GetTracksAsync(line, ct);
        var direction = dir.Trim().ToUpperInvariant();
        if (tracks.Main(direction)?.FindStop(stop) is not { } target)
            return Results.Problem($"Stop {stop} is not on line {line} direction {direction}.", statusCode: StatusCodes.Status404NotFound);

        return await WithLiveAsync(line, data, clock, ct, live =>
        {
            var arrivals = Arrivals.ForStop(stop, direction, live.Snapshot.Buses, tracks);
            return Results.Ok(new StopArrivalsResponse(
                line, direction, new StopRef(target.StopCode, target.Seq, target.Name), Freshness.From(live, clock),
                arrivals.Approaching.Select(ToArrivalView).ToList(),
                arrivals.RecentlyPassed.Select(ToView).ToList()));
        });
    }

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
    }

    private static TrackedBusView ToView(TrackedBus t) =>
        new(t.Bus.DoorNo, t.Bus.Lat, t.Bus.Lng, t.Bus.Direction, t.Bus.Variant, t.Bus.RouteCode, t.Bus.Destination,
            t.Bus.ReportedAt, t.AgeSeconds, t.Status, t.TripId, t.OnOwnVariant,
            Round(t.AlongMeters), Round(t.OffsetMeters), Round(t.RouteLengthMeters),
            t.LastStop?.Seq, t.NextStop?.Seq, Progress(t),
            t.SpeedMps is { } mps ? Math.Round(mps * 3.6, 1) : null,
            t.PassedSinceLastUpdate.Select(s => s.Seq).ToList());

    internal static ArrivalView ToArrivalView(Arrival a) =>
        new(a.Bus.Bus.DoorNo, a.Bus.Bus.RouteCode, a.Bus.Bus.Variant, a.StopsAway, Math.Round(a.DistanceMeters),
            a.EtaSeconds, a.NotDeparted, a.Bus.AgeSeconds, a.Bus.LastStop?.Seq);

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
    int? LastStopSeq, int? NextStopSeq, double? ProgressToNext, double? SpeedKmh, IReadOnlyList<int> PassedStopSeqs);

public sealed record LineLiveResponse(string Line, Freshness Data, IReadOnlyList<TrackedBusView> Buses);

public sealed record StopRef(string Code, int Seq, string Name);

public sealed record ArrivalView(
    string DoorNo, string RouteCode, string Variant, int StopsAway, double DistanceMeters, int? EtaSeconds,
    bool NotDeparted, int AgeSeconds, int? LastStopSeq);

public sealed record PassedView(string DoorNo, string RouteCode, string Variant, double MetersPast, int? LastStopSeq);

public sealed record StopArrivalsResponse(
    string Line, string Direction, StopRef Stop, Freshness Data,
    IReadOnlyList<ArrivalView> Approaching, IReadOnlyList<PassedView> RecentlyPassed);
