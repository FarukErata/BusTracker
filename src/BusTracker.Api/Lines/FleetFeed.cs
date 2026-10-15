using BusTracker.Api.Configuration;
using BusTracker.Api.Ibb;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Lines;

public sealed record FleetSnapshot(IReadOnlyDictionary<string, FleetVehicle> Vehicles, DateTimeOffset FetchedAt);

public sealed class FleetFeed(
    IbbClient ibb,
    FleetCallGate gate,
    IOptions<BusTrackerOptions> options,
    TimeProvider clock,
    ILogger<FleetFeed> logger)
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(options.Value.Polling.FleetIntervalSeconds);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private FleetSnapshot? _latest;
    private DateTimeOffset _lastCall = DateTimeOffset.MinValue;

    public FleetSnapshot? Latest => _latest;

    public TimeSpan Interval => _interval;

    public async Task<FleetSnapshot?> GetAsync(CancellationToken ct)
    {
        if (!IsDue()) return _latest;

        await _lock.WaitAsync(ct);
        try
        {
            return IsDue() ? await RefreshAsync(ct) : _latest;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsDue() => clock.GetUtcNow() - _lastCall >= _interval;

    private async Task<FleetSnapshot?> RefreshAsync(CancellationToken ct)
    {
        if (!gate.TryAcquire(FleetCallKind.Fleet).Allowed) return _latest;

        var now = clock.GetUtcNow();
        _lastCall = now;
        try
        {
            var vehicles = await ibb.GetFleetAsync(now, ct);
            gate.ReportSuccess();
            _latest = new FleetSnapshot(Newest(vehicles), now);
        }
        catch (IbbRateLimitedException)
        {
            gate.ReportRateLimited();
            logger.LogWarning("İBB rate limit hit while fetching the fleet; backing off until {Until}", gate.BlockedUntil);
        }
        catch (Exception e) when (e is IbbException or HttpRequestException || e is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(e, "Fleet fetch failed; keeping the previous positions");
        }
        return _latest;
    }

    private static Dictionary<string, FleetVehicle> Newest(IReadOnlyList<FleetVehicle> vehicles) =>
        vehicles.GroupBy(v => v.DoorNo).ToDictionary(g => g.Key, g => g.MaxBy(v => v.ReportedAt)!);
}
