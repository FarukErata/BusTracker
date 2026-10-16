using BusTracker.Api.Lines;
using BusTracker.Api.Shapes;

namespace BusTracker.Api.Endpoints;

public static class HealthEndpoints
{
    private static readonly DateTimeOffset StartedAt = new(System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime());

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", GetHealth).AllowAnonymous();
        return app;
    }

    private static async Task<HealthResponse> GetHealth(
        IConfiguration configuration, FleetCallGate gate, FleetFeed fleet, BusLineMemory memory,
        RouteShapeRepository shapes, CancellationToken ct)
    {
        var source = await shapes.GetSourceStateAsync(ct);
        var calls = gate.CallsInLastHour;
        return new HealthResponse(
            Status: "ok",
            Commit: configuration["BUSTRACKER_COMMIT"],
            StartedAt: StartedAt,
            UptimeSeconds: (long)(DateTimeOffset.UtcNow - StartedAt).TotalSeconds,
            WorkingSetMb: Environment.WorkingSet / (1024 * 1024),
            IbbCallsLastHour: new IbbCallsHealth(calls.Total, calls.Fleet, calls.Roster),
            FleetFetchedAt: fleet.Latest?.FetchedAt,
            FleetVehicles: fleet.Latest?.Vehicles.Count ?? 0,
            KnownBuses: memory.Count,
            FleetBlockedUntil: gate.BlockedUntil,
            RouteShapes: new RouteShapesHealth(
                await shapes.CountAsync(ct), source?.ETag, source?.LastModified, source?.CheckedAt, source?.ImportedAt));
    }
}

public sealed record RouteShapesHealth(
    int Count, string? SourceETag, DateTimeOffset? SourceLastModified, DateTimeOffset? CheckedAt, DateTimeOffset? ImportedAt);

public sealed record HealthResponse(
    string Status,
    string? Commit,
    DateTimeOffset StartedAt,
    long UptimeSeconds,
    long WorkingSetMb,
    IbbCallsHealth IbbCallsLastHour,
    DateTimeOffset? FleetFetchedAt,
    int FleetVehicles,
    int KnownBuses,
    DateTimeOffset? FleetBlockedUntil,
    RouteShapesHealth RouteShapes);

public sealed record IbbCallsHealth(int Total, int Fleet, int Roster);
