using BusTracker.Api.Lines;
using BusTracker.Api.Shapes;

namespace BusTracker.Api.Endpoints;

public static class HealthEndpoints
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", GetHealth).AllowAnonymous();
        return app;
    }

    private static async Task<HealthResponse> GetHealth(
        TimeProvider clock, FleetCallGate gate, RouteShapeRepository shapes, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var source = await shapes.GetSourceStateAsync(ct);
        return new HealthResponse(
            Status: "ok",
            StartedAt: StartedAt,
            UptimeSeconds: (long)(now - StartedAt).TotalSeconds,
            WorkingSetMb: Environment.WorkingSet / (1024 * 1024),
            FleetCallsLastMinute: gate.CallsInLastMinute,
            FleetBlockedUntil: gate.BlockedUntil,
            RouteShapes: new RouteShapesHealth(
                await shapes.CountAsync(ct), source?.ETag, source?.LastModified, source?.CheckedAt, source?.ImportedAt));
    }
}

public sealed record RouteShapesHealth(
    int Count, string? SourceETag, DateTimeOffset? SourceLastModified, DateTimeOffset? CheckedAt, DateTimeOffset? ImportedAt);

public sealed record HealthResponse(
    string Status,
    DateTimeOffset StartedAt,
    long UptimeSeconds,
    long WorkingSetMb,
    int FleetCallsLastMinute,
    DateTimeOffset? FleetBlockedUntil,
    RouteShapesHealth RouteShapes);
