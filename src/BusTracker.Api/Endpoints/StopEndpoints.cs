using BusTracker.Api.Stops;
using BusTracker.Api.Timetables;
using BusTracker.Api.Tracing;

namespace BusTracker.Api.Endpoints;

public static class StopEndpoints
{
    private const int SearchLimit = 20;

    public static IEndpointRouteBuilder MapStopEndpoints(this IEndpointRouteBuilder app)
    {
        var stops = app.MapGroup("/api/stops");
        stops.MapGet("/", Search);
        stops.MapGet("/{code}", GetStop);
        stops.MapGet("/{code}/arrivals", GetArrivals);
        return app;
    }

    private static async Task<IReadOnlyList<StopSearchResult>> Search(string? q, StopIndexRepository index, CancellationToken ct) =>
        (await index.SearchAsync(q ?? "", SearchLimit, ct)).Select(r => new StopSearchResult(r.Stop, r.Lines)).ToList();

    private static IReadOnlySet<string>? WantedLines(string? lines)
    {
        var codes = (lines ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(LineCode.Normalize).OfType<string>().ToHashSet();
        return codes.Count > 0 ? codes : null;
    }

    private static async Task<IResult> GetStop(string code, StopIndexRepository index, CancellationToken ct)
    {
        if (await index.GetStopAsync(code.Trim(), ct) is not { } stop) return UnknownStop(code);
        return Results.Ok(new StopDetails(stop, await index.GetLinesAtStopAsync(stop.Code, ct)));
    }

    private static async Task<IResult> GetArrivals(
        string code, string? lines, StopArrivalsService arrivals, LinePlans plans, TimeProvider clock, CancellationToken ct)
    {
        var wanted = WantedLines(lines);
        if (await arrivals.GetAsync(code.Trim(), wanted, ct) is not { } result) return UnknownStop(code);

        var now = clock.GetUtcNow();
        var linePlans = await plans.GetManyAsync(
            result.Lines.Select(l => l.Line.LineCode).Where(l => wanted is null || wanted.Contains(l)), ct);
        var rows = result.Lines
            .Select(l => (Line: l, Plan: linePlans.GetValueOrDefault(l.Line.LineCode)?.For(l.Line.Direction, result.Stop.Code, LinePlans.WaitingAmong(l.Arrivals), now)))
            .ToList();

        var merged = rows
            .SelectMany(r => r.Line.Arrivals.Select(a => new StopArrivalView(r.Line.Line.LineCode, r.Line.Line.Direction, r.Line.Line.DirectionName,
                LiveEndpoints.ToArrivalView(a) with { Planned = r.Plan?.Waiting.GetValueOrDefault(a.Bus.Bus.DoorNo) })))
            .OrderBy(a => a.Bus.EtaSeconds ?? int.MaxValue)
            .ThenBy(a => a.Bus.DistanceMeters)
            .ToList();
        var statuses = rows
            .Select(r => new LineStatusView(r.Line.Line.LineCode, r.Line.Line.Direction, r.Line.Line.DirectionName,
                r.Line.Live is null ? null : Freshness.From(r.Line.Live, clock), r.Line.Error, RetryInSeconds(r.Line.RetryAt, clock), r.Plan?.View))
            .ToList();
        var announcements = linePlans.Values.SelectMany(p => p.Announcements).ToList();
        Telemetry.Note("lines", statuses.Count);
        Telemetry.Note("waitingLines", statuses.Count(l => l.Error is not null));
        Telemetry.Note("waiting", string.Join(", ", statuses.Where(l => l.Error is not null).Select(l => $"{l.Line} {l.Error}")));
        Telemetry.Note("etas", EtaNote.Of(merged.Select(a => (a.Line, a.Bus))));
        return Results.Ok(new StopArrivalsView(result.Stop, merged, statuses, announcements));
    }

    private static int? RetryInSeconds(DateTimeOffset? retryAt, TimeProvider clock) =>
        retryAt is { } at ? Math.Max(1, (int)Math.Ceiling((at - clock.GetUtcNow()).TotalSeconds)) : null;

    private static IResult UnknownStop(string code) =>
        Results.Problem($"Stop {code} is not in the stop index.", statusCode: StatusCodes.Status404NotFound);
}

public static class EtaNote
{
    private const int Shown = 6;

    public static string Of(IEnumerable<(string Line, ArrivalView Bus)> arrivals) =>
        string.Join(", ", arrivals.Take(Shown).Select(a => $"{a.Line} {a.Bus.DoorNo} {a.Bus.StopsAway}st {(a.Bus.EtaSeconds is { } s ? $"{s / 60}m" : "-")}"));
}

public sealed record StopSearchResult(StopInfo Stop, IReadOnlyList<string> Lines);

public sealed record StopDetails(StopInfo Stop, IReadOnlyList<StopLine> Lines);

public sealed record StopArrivalView(string Line, string Direction, string DirectionName, ArrivalView Bus);

public sealed record LineStatusView(
    string Line, string Direction, string DirectionName, Freshness? Data, string? Error, int? RetryInSeconds, PlannedView? Planned);

public sealed record StopArrivalsView(
    StopInfo Stop, IReadOnlyList<StopArrivalView> Arrivals, IReadOnlyList<LineStatusView> Lines, IReadOnlyList<AnnouncementView> Announcements);
