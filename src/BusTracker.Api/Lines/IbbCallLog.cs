using System.Threading.Channels;
using BusTracker.Api.Data;
using Dapper;

namespace BusTracker.Api.Lines;

public sealed class IbbCallLog(Database database, FleetCallGate gate, TimeProvider clock, ILogger<IbbCallLog> logger) : BackgroundService
{
    private const string BlockedKind = "blocked";
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly Channel<Entry> _entries = Channel.CreateUnbounded<Entry>(new UnboundedChannelOptions { SingleReader = true });

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RestoreAsync(cancellationToken);
        gate.CallRecorded += (at, kind) => _entries.Writer.TryWrite(new Entry(at, kind.ToString().ToLowerInvariant()));
        gate.Blocked += until => _entries.Writer.TryWrite(new Entry(until, BlockedKind));
        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _entries.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var entry in _entries.Reader.ReadAllAsync(CancellationToken.None))
        {
            try
            {
                await SaveAsync(entry);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Could not save an İBB call record");
            }
        }
    }

    private async Task RestoreAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var connection = await database.OpenAsync(ct);
        var calls = await connection.QueryAsync<CallRow>(
            "SELECT at AS At, kind AS Kind FROM ibb_calls WHERE at >= @Since;", new { Since = DbTime.Format(now - Window) });
        var blocked = await connection.ExecuteScalarAsync<string?>("SELECT value FROM ibb_state WHERE name = @Name;", new { Name = BlockedKind });

        var restored = calls
            .Where(c => Enum.TryParse<FleetCallKind>(c.Kind, ignoreCase: true, out _))
            .Select(c => (DbTime.Parse(c.At)!.Value, Enum.Parse<FleetCallKind>(c.Kind, ignoreCase: true)))
            .ToList();
        gate.Restore(restored, DbTime.Parse(blocked));
        if (restored.Count > 0) logger.LogInformation("Restored {Count} İBB calls from the last hour", restored.Count);
    }

    private async Task SaveAsync(Entry entry)
    {
        await using var connection = await database.OpenAsync(CancellationToken.None);
        if (entry.Kind == BlockedKind)
        {
            await connection.ExecuteAsync(
                "INSERT INTO ibb_state (name, value) VALUES (@Name, @Value) ON CONFLICT (name) DO UPDATE SET value = excluded.value;",
                new { Name = BlockedKind, Value = DbTime.Format(entry.At) });
            return;
        }

        await connection.ExecuteAsync(
            "INSERT INTO ibb_calls (at, kind) VALUES (@At, @Kind); DELETE FROM ibb_calls WHERE at < @Since;",
            new { At = DbTime.Format(entry.At), entry.Kind, Since = DbTime.Format(entry.At - Window) });
    }

    private sealed record Entry(DateTimeOffset At, string Kind);

    private sealed record CallRow(string At, string Kind);
}
