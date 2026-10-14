using System.Collections.Concurrent;
using BusTracker.Api.Configuration;
using BusTracker.Api.Ibb;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Lines;

public sealed record LiveSnapshot(IReadOnlyList<LiveBus> Buses, DateTimeOffset FetchedAt);

public sealed record LiveResult(LiveSnapshot Snapshot, bool Stale, string? StaleReason, DateTimeOffset? RetryAt);

public sealed class LineUnavailableException(string reason, DateTimeOffset? retryAt)
    : Exception($"Live data unavailable: {reason}")
{
    public string Reason { get; } = reason;
    public DateTimeOffset? RetryAt { get; } = retryAt;
}

public sealed class LineDataService(
    IbbClient ibb,
    FleetCallGate gate,
    IOptions<BusTrackerOptions> options,
    TimeProvider clock,
    ILogger<LineDataService> logger)
{
    private readonly BusTrackerOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, (IReadOnlyList<RouteStop> Stops, DateTimeOffset FetchedAt)> _stops = new();
    private readonly ConcurrentDictionary<string, LiveSnapshot> _live = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lineLocks = new();

    public async Task<IReadOnlyList<RouteStop>> GetStopsAsync(string lineCode, CancellationToken ct)
    {
        var maxAge = TimeSpan.FromHours(_options.StaticData.RefreshHours);
        if (_stops.TryGetValue(lineCode, out var cached) && clock.GetUtcNow() - cached.FetchedAt < maxAge)
            return cached.Stops;

        var stops = await ibb.GetRouteStopsAsync(lineCode, ct);
        _stops[lineCode] = (stops, clock.GetUtcNow());
        return stops;
    }

    public async Task<LiveResult> GetLiveAsync(string lineCode, CancellationToken ct)
    {
        if (TryGetFresh(lineCode) is { } fresh) return new LiveResult(fresh, false, null, null);

        var lineLock = _lineLocks.GetOrAdd(lineCode, _ => new SemaphoreSlim(1, 1));
        await lineLock.WaitAsync(ct);
        try
        {
            return TryGetFresh(lineCode) is { } fetchedMeanwhile
                ? new LiveResult(fetchedMeanwhile, false, null, null)
                : await FetchLiveAsync(lineCode, ct);
        }
        finally
        {
            lineLock.Release();
        }
    }

    private LiveSnapshot? TryGetFresh(string lineCode)
    {
        var interval = TimeSpan.FromSeconds(_options.Polling.LineIntervalSeconds);
        return _live.TryGetValue(lineCode, out var snapshot) && clock.GetUtcNow() - snapshot.FetchedAt < interval
            ? snapshot
            : null;
    }

    private async Task<LiveResult> FetchLiveAsync(string lineCode, CancellationToken ct)
    {
        var decision = gate.TryAcquire();
        if (!decision.Allowed) return FromCache(lineCode, decision.Reason!, decision.RetryAt);

        try
        {
            var buses = await ibb.GetLiveBusesAsync(lineCode, ct);
            gate.ReportSuccess();
            var snapshot = new LiveSnapshot(buses, clock.GetUtcNow());
            _live[lineCode] = snapshot;
            return new LiveResult(snapshot, false, null, null);
        }
        catch (IbbRateLimitedException)
        {
            gate.ReportRateLimited();
            logger.LogWarning("İBB rate limit hit while fetching {Line}; backing off until {Until}", lineCode, gate.BlockedUntil);
            return FromCache(lineCode, "rate-limited", gate.BlockedUntil);
        }
    }

    private LiveResult FromCache(string lineCode, string reason, DateTimeOffset? retryAt) =>
        _live.TryGetValue(lineCode, out var cached)
            ? new LiveResult(cached, true, reason, retryAt)
            : throw new LineUnavailableException(reason, retryAt);
}
