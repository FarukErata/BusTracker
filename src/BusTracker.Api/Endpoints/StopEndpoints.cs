using BusTracker.Api.Stops;
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

    private static async Task<IResult> GetArrivals(string code, string? lines, StopArrivalsService arrivals, TimeProvider clock, CancellationToken ct)
    {
        if (await arrivals.GetAsync(code.Trim(), WantedLines(lines), ct) is not { } result) return UnknownStop(code);

        var merged = result.Lines
            .SelectMany(l => l.Arrivals.Select(a => new StopArrivalView(l.Line.LineCode, l.Line.Direction, l.Line.DirectionName, LiveEndpoints.ToArrivalView(a))))
            .OrderBy(a => a.Bus.EtaSeconds ?? int.MaxValue)
            .ThenBy(a => a.Bus.DistanceMeters)
            .ToList();
        var statuses = result.Lines
            .Select(l => new LineStatusView(l.Line.LineCode, l.Line.Direction, l.Line.DirectionName,
                l.Live is null ? null : Freshness.From(l.Live, clock), l.Error, RetryInSeconds(l.RetryAt, clock)))
            .ToList();
        Telemetry.Note("lines", statuses.Count);
        Telemetry.Note("waitingLines", statuses.Count(l => l.Error is not null));
        return Results.Ok(new StopArrivalsView(result.Stop, merged, statuses));
    }

    private static int? RetryInSeconds(DateTimeOffset? retryAt, TimeProvider clock) =>
        retryAt is { } at ? Math.Max(1, (int)Math.Ceiling((at - clock.GetUtcNow()).TotalSeconds)) : null;

    private static IResult UnknownStop(string code) =>
        Results.Problem($"Stop {code} is not in the stop index.", statusCode: StatusCodes.Status404NotFound);
}

public sealed record StopSearchResult(StopInfo Stop, IReadOnlyList<string> Lines);

public sealed record StopDetails(StopInfo Stop, IReadOnlyList<StopLine> Lines);

public sealed record StopArrivalView(string Line, string Direction, string DirectionName, ArrivalView Bus);

public sealed record LineStatusView(string Line, string Direction, string DirectionName, Freshness? Data, string? Error, int? RetryInSeconds);

public sealed record StopArrivalsView(StopInfo Stop, IReadOnlyList<StopArrivalView> Arrivals, IReadOnlyList<LineStatusView> Lines);
