using System.Threading.Channels;
using BusTracker.Api.Data;
using BusTracker.Api.Tracking;
using Dapper;

namespace BusTracker.Api.Lines;

public sealed class HopTimeLog(Database database, HopTimes hops, TimeProvider clock, ILogger<HopTimeLog> logger) : BackgroundService
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(60);
    private static readonly TimeSpan PruneEvery = TimeSpan.FromHours(6);
    private const int BatchSize = 500;

    private readonly Channel<HopObservation> _observations = Channel.CreateBounded<HopObservation>(
        new BoundedChannelOptions(50_000) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private DateTimeOffset _prunedAt = DateTimeOffset.MinValue;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RestoreAsync(cancellationToken);
        hops.Recorded += observation => _observations.Writer.TryWrite(observation);
        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _observations.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _observations.Reader.WaitToReadAsync(CancellationToken.None))
        {
            var batch = new List<HopObservation>();
            while (batch.Count < BatchSize && _observations.Reader.TryRead(out var observation)) batch.Add(observation);
            try
            {
                await SaveAsync(batch);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Could not save {Count} hop times", batch.Count);
            }
        }
    }

    private async Task RestoreAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        var rows = await connection.QueryAsync<HopRow>(
            "SELECT route_code AS RouteCode, from_stop AS FromStop, observed_at AS ObservedAt, seconds AS Seconds FROM hop_times WHERE observed_at >= @Since;",
            new { Since = DbTime.Format(clock.GetUtcNow() - HopTimes.RecentWindow) });
        hops.Restore(rows.Select(r => new HopObservation(r.RouteCode, (int)r.FromStop, DbTime.Parse(r.ObservedAt)!.Value, r.Seconds)));
    }

    private async Task SaveAsync(IReadOnlyList<HopObservation> batch)
    {
        await using var connection = await database.OpenAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO hop_times (route_code, from_stop, observed_at, seconds) VALUES (@RouteCode, @FromStop, @ObservedAt, @Seconds);",
            batch.Select(o => new { o.RouteCode, o.FromStop, ObservedAt = DbTime.Format(o.At), Seconds = Math.Round(o.Seconds, 1) }),
            transaction);

        var now = clock.GetUtcNow();
        if (now - _prunedAt >= PruneEvery)
        {
            await connection.ExecuteAsync("DELETE FROM hop_times WHERE observed_at < @Before;",
                new { Before = DbTime.Format(now - KeepFor) }, transaction);
            _prunedAt = now;
        }
        await transaction.CommitAsync();
    }

    private sealed record HopRow(string RouteCode, long FromStop, string ObservedAt, double Seconds);
}
