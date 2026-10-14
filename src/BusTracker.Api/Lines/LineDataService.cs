using System.Collections.Concurrent;
using BusTracker.Api.Configuration;
using BusTracker.Api.Ibb;
using BusTracker.Api.Shapes;
using BusTracker.Api.Tracking;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Lines;

public sealed record LiveSnapshot(IReadOnlyList<TrackedBus> Buses, DateTimeOffset FetchedAt);

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
    RouteShapeRepository shapes,
    IOptions<BusTrackerOptions> options,
    TimeProvider clock,
    ILogger<LineDataService> logger)
{
    private readonly BusTrackerOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, Cached<IReadOnlyList<RouteStop>>> _stops = new();
    private readonly ConcurrentDictionary<string, Cached<LineTracks>> _tracks = new();
    private readonly ConcurrentDictionary<string, LiveSnapshot> _live = new();
    private readonly ConcurrentDictionary<string, LineTracker> _trackers = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lineLocks = new();

    private TimeSpan StaticMaxAge => TimeSpan.FromHours(_options.StaticData.RefreshHours);

    public async Task<IReadOnlyList<RouteStop>> GetStopsAsync(string lineCode, CancellationToken ct)
    {
        if (TryGetFresh(_stops, lineCode) is { } cached) return cached;

        var stops = await ibb.GetRouteStopsAsync(lineCode, ct);
        _stops[lineCode] = new Cached<IReadOnlyList<RouteStop>>(stops, clock.GetUtcNow());
        return stops;
    }

    public async Task<LineTracks> GetTracksAsync(string lineCode, CancellationToken ct)
    {
        if (TryGetFresh(_tracks, lineCode) is { } cached) return cached;

        var stops = await GetStopsAsync(lineCode, ct);
        var lineShapes = await shapes.GetForLineAsync(lineCode, ct);
        var tracks = stops.Count == 0 ? LineTracks.Empty : LineTracks.Build(stops, lineShapes, _options.Tracking.StopOnRouteMeters);
        _tracks[lineCode] = new Cached<LineTracks>(tracks, clock.GetUtcNow());
        return tracks;
    }

    public async Task<LiveResult> GetLiveAsync(string lineCode, CancellationToken ct)
    {
        if (TryGetFreshLive(lineCode) is { } fresh) return new LiveResult(fresh, false, null, null);

        var lineLock = _lineLocks.GetOrAdd(lineCode, _ => new SemaphoreSlim(1, 1));
        await lineLock.WaitAsync(ct);
        try
        {
            return TryGetFreshLive(lineCode) is { } fetchedMeanwhile
                ? new LiveResult(fetchedMeanwhile, false, null, null)
                : await FetchLiveAsync(lineCode, ct);
        }
        finally
        {
            lineLock.Release();
        }
    }

    private T? TryGetFresh<T>(ConcurrentDictionary<string, Cached<T>> cache, string lineCode) where T : class =>
        cache.TryGetValue(lineCode, out var cached) && clock.GetUtcNow() - cached.At < StaticMaxAge ? cached.Value : null;

    private LiveSnapshot? TryGetFreshLive(string lineCode)
    {
        var interval = TimeSpan.FromSeconds(_options.Polling.LineIntervalSeconds);
        return _live.TryGetValue(lineCode, out var snapshot) && clock.GetUtcNow() - snapshot.FetchedAt < interval
            ? snapshot
            : null;
    }

    private async Task<LiveResult> FetchLiveAsync(string lineCode, CancellationToken ct)
    {
        var tracks = await GetTracksAsync(lineCode, ct);
        var decision = gate.TryAcquire();
        if (!decision.Allowed) return FromCache(lineCode, decision.Reason!, decision.RetryAt);

        try
        {
            var buses = await ibb.GetLiveBusesAsync(lineCode, ct);
            gate.ReportSuccess();
            var now = clock.GetUtcNow();
            var tracker = _trackers.GetOrAdd(lineCode, _ => new LineTracker(_options.Tracking));
            var snapshot = new LiveSnapshot(tracker.Update(buses, tracks, now), now);
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

    private sealed record Cached<T>(T Value, DateTimeOffset At);
}
