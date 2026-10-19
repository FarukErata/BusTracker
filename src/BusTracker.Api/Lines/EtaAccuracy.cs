using System.Collections.Concurrent;
using System.Threading.Channels;
using BusTracker.Api.Data;
using BusTracker.Api.Tracking;
using Dapper;

namespace BusTracker.Api.Lines;

public sealed record EtaPrediction(
    string RouteCode, string DoorNo, int StopIndex, DateTimeOffset PredictedAt,
    int SpeedSeconds, int HopSeconds, double DistanceMeters, int AgeSeconds);

public sealed record EtaModelScore(int Count, double BiasSeconds, double TypicalMissSeconds, double WithinOneMinute, double WithinTwoMinutes);

public sealed record EtaBucket(string Label, EtaModelScore Speed, EtaModelScore Hops);

public sealed record EtaSummary(DateTimeOffset Since, int Count, IReadOnlyList<EtaBucket> ByDistance, IReadOnlyList<EtaBucket> ByHour);

public sealed class EtaAccuracy(Database database, TimeProvider clock, ILogger<EtaAccuracy> logger) : BackgroundService
{
    private static readonly TimeSpan PendingFor = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(50);
    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(60);
    private static readonly TimeSpan Istanbul = TimeSpan.FromHours(3);
    private const int MaxPending = 50_000;
    private const int MaxPerKey = 40;
    private const int MaxSummaryRows = 300_000;

    private static readonly (string Label, double From, double To)[] Horizons =
        [("under 3 min", 0, 180), ("3–7 min", 180, 420), ("7–15 min", 420, 900), ("15 min or more", 900, double.MaxValue)];

    private readonly ConcurrentDictionary<(string RouteCode, string DoorNo, int StopIndex), List<EtaPrediction>> _pending = new();
    private readonly Channel<(EtaPrediction Prediction, double ActualSeconds)> _checks =
        Channel.CreateBounded<(EtaPrediction, double)>(new BoundedChannelOptions(50_000) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private long _sweptAtTicks;
    private DateTimeOffset _prunedAt = DateTimeOffset.MinValue;

    public void Predicted(EtaPrediction prediction)
    {
        SweepIfDue(prediction.PredictedAt);
        if (_pending.Count >= MaxPending) return;

        var list = _pending.GetOrAdd((prediction.RouteCode, prediction.DoorNo, prediction.StopIndex), _ => []);
        lock (list)
        {
            if (list.Count > 0 && prediction.PredictedAt - list[^1].PredictedAt < SampleEvery) return;
            list.Add(prediction);
            if (list.Count > MaxPerKey) list.RemoveAt(0);
        }
    }

    public void Crossed(StopCrossing crossing)
    {
        if (!_pending.TryRemove((crossing.RouteCode, crossing.DoorNo, crossing.StopIndex), out var list)) return;
        lock (list)
        {
            foreach (var prediction in list.Where(p => p.PredictedAt <= crossing.At && crossing.At - p.PredictedAt <= PendingFor))
                _checks.Writer.TryWrite((prediction, (crossing.At - prediction.PredictedAt).TotalSeconds));
        }
    }

    public async Task<EtaSummary> SummaryAsync(int days, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-days);
        await using var connection = await database.OpenAsync(ct);
        var rows = (await connection.QueryAsync<CheckRow>(
            """
            SELECT predicted_at AS PredictedAt, actual_s AS ActualSeconds, eta_speed AS SpeedSeconds, eta_hops AS HopSeconds
            FROM eta_checks WHERE predicted_at >= @Since ORDER BY predicted_at DESC LIMIT @Limit;
            """,
            new { Since = DbTime.Format(since), Limit = MaxSummaryRows })).ToList();

        var byDistance = Horizons
            .Select(h => Bucket(h.Label, rows.Where(r => r.ActualSeconds >= h.From && r.ActualSeconds < h.To).ToList()))
            .Where(b => b.Speed.Count > 0)
            .ToList();
        var byHour = rows
            .GroupBy(r => (DbTime.Parse(r.PredictedAt)!.Value.ToOffset(Istanbul)).Hour)
            .OrderBy(g => g.Key)
            .Select(g => Bucket($"{g.Key:00}:00", g.ToList()))
            .ToList();
        return new EtaSummary(since, rows.Count, byDistance, byHour);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _checks.Reader.WaitToReadAsync(stoppingToken))
                await SaveBatchAsync();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        while (_checks.Reader.Count > 0) await SaveBatchAsync();
    }

    private async Task SaveBatchAsync()
    {
        var batch = new List<(EtaPrediction Prediction, double ActualSeconds)>();
        while (batch.Count < 500 && _checks.Reader.TryRead(out var check)) batch.Add(check);
        try
        {
            await SaveAsync(batch);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not save {Count} ETA checks", batch.Count);
        }
    }

    private async Task SaveAsync(IReadOnlyList<(EtaPrediction Prediction, double ActualSeconds)> batch)
    {
        await using var connection = await database.OpenAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO eta_checks (route_code, stop_index, predicted_at, actual_s, eta_speed, eta_hops, distance_m, age_s)
            VALUES (@RouteCode, @StopIndex, @PredictedAt, @ActualSeconds, @SpeedSeconds, @HopSeconds, @DistanceMeters, @AgeSeconds);
            """,
            batch.Select(c => new
            {
                c.Prediction.RouteCode, c.Prediction.StopIndex, PredictedAt = DbTime.Format(c.Prediction.PredictedAt),
                ActualSeconds = Math.Round(c.ActualSeconds, 1), c.Prediction.SpeedSeconds, c.Prediction.HopSeconds,
                DistanceMeters = Math.Round(c.Prediction.DistanceMeters), c.Prediction.AgeSeconds,
            }),
            transaction);

        var now = clock.GetUtcNow();
        if (now - _prunedAt >= TimeSpan.FromHours(6))
        {
            await connection.ExecuteAsync("DELETE FROM eta_checks WHERE predicted_at < @Before;", new { Before = DbTime.Format(now - KeepFor) }, transaction);
            _prunedAt = now;
        }
        await transaction.CommitAsync();
    }

    private void SweepIfDue(DateTimeOffset now)
    {
        var last = new DateTimeOffset(Interlocked.Read(ref _sweptAtTicks), TimeSpan.Zero);
        if (now - last < SweepEvery || Interlocked.CompareExchange(ref _sweptAtTicks, now.UtcTicks, last.UtcTicks) != last.UtcTicks) return;

        foreach (var (key, list) in _pending)
        {
            bool expired;
            lock (list) expired = list.Count == 0 || now - list[^1].PredictedAt > PendingFor;
            if (expired) _pending.TryRemove(key, out _);
        }
    }

    private static EtaBucket Bucket(string label, IReadOnlyList<CheckRow> rows) =>
        new(label, Score(rows, r => r.SpeedSeconds), Score(rows, r => r.HopSeconds));

    private static EtaModelScore Score(IReadOnlyList<CheckRow> rows, Func<CheckRow, long> predicted)
    {
        if (rows.Count == 0) return new EtaModelScore(0, 0, 0, 0, 0);
        var errors = rows.Select(r => predicted(r) - r.ActualSeconds).ToList();
        var misses = errors.Select(Math.Abs).Order().ToList();
        return new EtaModelScore(
            rows.Count,
            Math.Round(errors.Average()),
            Math.Round(misses[misses.Count / 2]),
            Math.Round(misses.Count(m => m <= 60) / (double)misses.Count, 2),
            Math.Round(misses.Count(m => m <= 120) / (double)misses.Count, 2));
    }

    private sealed record CheckRow(string PredictedAt, double ActualSeconds, long SpeedSeconds, long HopSeconds);
}
