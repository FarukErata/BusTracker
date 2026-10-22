using System.Text.Json;
using BusTracker.Api.Stops;

namespace BusTracker.Api.Rail;

public sealed record RailData(
    IReadOnlyDictionary<int, RailLine> Lines,
    IReadOnlyDictionary<int, RailStation> Stations,
    IReadOnlyDictionary<int, IReadOnlyList<RailDirection>> Directions,
    DateTimeOffset LoadedAt)
{
    public static readonly RailData Empty = new(
        new Dictionary<int, RailLine>(), new Dictionary<int, RailStation>(), new Dictionary<int, IReadOnlyList<RailDirection>>(), DateTimeOffset.MinValue);
}

public sealed class RailCatalog
{
    private volatile RailData _data = RailData.Empty;
    private volatile IReadOnlyDictionary<int, IReadOnlyList<RailNotice>> _notices = new Dictionary<int, IReadOnlyList<RailNotice>>();

    public RailData Data => _data;

    public RailStation? Station(int id) => _data.Stations.GetValueOrDefault(id);

    public RailLine? Line(int id) => _data.Lines.GetValueOrDefault(id);

    public IReadOnlyList<RailDirection> Directions(int lineId) => _data.Directions.GetValueOrDefault(lineId) ?? [];

    public IReadOnlyList<RailNotice> Notices(int lineId) => _notices.GetValueOrDefault(lineId) ?? [];

    public IReadOnlyList<RailStation> Search(string query, int limit)
    {
        var words = SearchText.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || words.Sum(w => w.Length) < 2) return [];
        return _data.Stations.Values
            .Select(s => (Station: s, Name: SearchText.Normalize(s.Name), Line: SearchText.Normalize(s.Line)))
            .Where(x => words.All(w => x.Name.Contains(w) || x.Line == w))
            .OrderBy(x => x.Name.StartsWith(words[0]) ? 0 : 1)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.Station.Line)
            .Take(limit)
            .Select(x => x.Station)
            .ToList();
    }

    public void Replace(RailData data) => _data = data;

    public void ReplaceNotices(IReadOnlyList<RailNotice> notices) =>
        _notices = notices.GroupBy(n => n.LineId).ToDictionary(g => g.Key, g => (IReadOnlyList<RailNotice>)g.ToList());
}

public sealed class RailSyncService(MetroClient metro, RailCatalog catalog, TimeProvider clock, ILogger<RailSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan CatalogEvery = TimeSpan.FromHours(24);
    private static readonly TimeSpan NoticesEvery = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var catalogDue = clock.GetUtcNow() - catalog.Data.LoadedAt >= CatalogEvery;
                var ok = (!catalogDue || await TryAsync("rail catalog", LoadCatalogAsync, stoppingToken))
                    & await TryAsync("rail notices", LoadNoticesAsync, stoppingToken);
                await Task.Delay(ok ? NoticesEvery : RetryAfter, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task LoadCatalogAsync(CancellationToken ct)
    {
        var lines = await metro.GetLinesAsync(ct);
        var stations = await metro.GetStationsAsync(ct);
        var directions = new Dictionary<int, IReadOnlyList<RailDirection>>();
        foreach (var line in lines)
            directions[line.Id] = await metro.GetDirectionsAsync(line.Id, ct);

        var known = lines.Select(l => l.Id).ToHashSet();
        catalog.Replace(new RailData(
            lines.ToDictionary(l => l.Id),
            stations.Where(s => known.Contains(s.LineId)).ToDictionary(s => s.Id),
            directions,
            clock.GetUtcNow()));
        logger.LogInformation("Loaded {Lines} rail lines and {Stations} stations", lines.Count, stations.Count);
    }

    private async Task LoadNoticesAsync(CancellationToken ct) => catalog.ReplaceNotices(await metro.GetServiceStatusesAsync(ct));

    private async Task<bool> TryAsync(string what, Func<CancellationToken, Task> load, CancellationToken ct)
    {
        try
        {
            await load(ct);
            return true;
        }
        catch (Exception e) when (e is MetroException or HttpRequestException or JsonException
            || e is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Loading {What} failed: {Error}", what, e.Message);
            return false;
        }
    }
}
