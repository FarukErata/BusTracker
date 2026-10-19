using BusTracker.Api.Lines;

namespace BusTracker.Api.Endpoints;

public static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats/eta", GetEta);
        return app;
    }

    private static Task<EtaSummary> GetEta(int? days, EtaAccuracy accuracy, CancellationToken ct) =>
        accuracy.SummaryAsync(Math.Clamp(days ?? 7, 1, 60), ct);
}
