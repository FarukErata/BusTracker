using BusTracker.Api.Configuration;
using BusTracker.Api.Ibb;
using BusTracker.Api.Shapes;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Stops;

public sealed class StopIndexSync(
    IbbClient ibb,
    StopIndexRepository repository,
    IOptions<BusTrackerOptions> options,
    TimeProvider clock,
    ILogger<StopIndexSync> logger)
{
    public async Task<SyncOutcome> SyncAsync(CancellationToken ct)
    {
        var (importedAt, count) = await repository.GetStateAsync(ct);
        var now = clock.GetUtcNow();
        if (count > 0 && importedAt is { } at && now - at < TimeSpan.FromHours(options.Value.StaticData.RefreshHours))
            return SyncOutcome.Unchanged;

        logger.LogInformation("Downloading the stop order of every line");
        var rows = await ibb.StreamAllRouteStopsAsync(ct).ToListAsync(ct);
        var imported = await repository.ReplaceAllAsync(rows.ToAsyncEnumerable(), Math.Max(1, (count ?? 0) / 2), now, ct);
        logger.LogInformation("Imported {Count} line stops into the stop index", imported);
        return SyncOutcome.Imported;
    }
}

public sealed class StaticDataSyncService(
    RouteShapeSync shapes,
    StopIndexSync stopIndex,
    IOptions<BusTrackerOptions> options,
    TimeProvider clock,
    ILogger<StaticDataSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.StaticData.SyncEnabled) return;

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        do
        {
            await RunAsync("stop index", stopIndex.SyncAsync, stoppingToken);
            await RunAsync("route shapes", shapes.SyncAsync, stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunAsync(string name, Func<CancellationToken, Task<SyncOutcome>> sync, CancellationToken ct)
    {
        try
        {
            await sync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Sync of {Name} failed; keeping the existing data", name);
        }
    }
}
