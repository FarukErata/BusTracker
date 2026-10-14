namespace BusTracker.Api.Endpoints;

public static class HealthEndpoints
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", GetHealth);
        return app;
    }

    private static HealthResponse GetHealth(TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return new HealthResponse(
            Status: "ok",
            StartedAt: StartedAt,
            UptimeSeconds: (long)(now - StartedAt).TotalSeconds,
            WorkingSetMb: Environment.WorkingSet / (1024 * 1024));
    }
}

public sealed record HealthResponse(string Status, DateTimeOffset StartedAt, long UptimeSeconds, long WorkingSetMb);
