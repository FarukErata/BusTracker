using System.Collections.Concurrent;
using BusTracker.Api.Ibb;

namespace BusTracker.Api.Timetables;

public sealed class TimetableService(IbbClient ibb, TimeProvider clock, ILogger<TimetableService> logger)
{
    private static readonly TimeSpan RetryAfterError = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryAfterEmpty = TimeSpan.FromHours(1);
    private const int MaxWarmedLines = 20;

    private readonly ConcurrentDictionary<string, Entry> _cache = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<Timetable?>>> _loading = new();

    public Task<Timetable?> GetAsync(string lineCode, CancellationToken ct)
    {
        if (_cache.TryGetValue(lineCode, out var entry) && clock.GetUtcNow() < entry.RefreshAt)
            return Task.FromResult(entry.Table);
        return _loading.GetOrAdd(lineCode, line => new Lazy<Task<Timetable?>>(() => LoadAsync(line))).Value.WaitAsync(ct);
    }

    public Timetable? Peek(string lineCode)
    {
        if (_cache.TryGetValue(lineCode, out var entry) && clock.GetUtcNow() < entry.RefreshAt) return entry.Table;
        _ = GetAsync(lineCode, CancellationToken.None);
        return entry?.Table;
    }

    public void Warm(IEnumerable<string> lineCodes)
    {
        foreach (var line in lineCodes.Distinct().Take(MaxWarmedLines))
            _ = GetAsync(line, CancellationToken.None);
    }

    private async Task<Timetable?> LoadAsync(string lineCode)
    {
        var previous = _cache.GetValueOrDefault(lineCode)?.Table;
        var now = clock.GetUtcNow();
        try
        {
            var trips = await ibb.GetPlannedTripsAsync(lineCode, CancellationToken.None);
            var table = trips.Count > 0 ? new Timetable(trips) : previous;
            _cache[lineCode] = new Entry(table, trips.Count > 0 ? ServiceDay.Of(now).Ends : now + RetryAfterEmpty);
            return table;
        }
        catch (Exception e) when (e is IbbException or HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning("Timetable for {Line} failed: {Error}", lineCode, e.Message);
            _cache[lineCode] = new Entry(previous, now + RetryAfterError);
            return previous;
        }
        finally
        {
            _loading.TryRemove(lineCode, out _);
        }
    }

    private sealed record Entry(Timetable? Table, DateTimeOffset RefreshAt);
}
