using System.Collections.Concurrent;
using System.Text.Json;
using BusTracker.Api.Stops;
using BusTracker.Api.Timetables;

namespace BusTracker.Api.Rail;

public sealed record RailLineView(int Id, string Name, string Color, string Description);

public sealed record RailLineResponse(RailLineView Line, IReadOnlyList<RailStationView> Stations, IReadOnlyList<RailNoticeView> Notices);

public sealed record RailStationView(int Id, string Name, int LineId, string Line, string Color, double? Lat, double? Lng);

public sealed record RailDepartureView(DateTimeOffset At, bool NextDay);

public sealed record RailTripView(string Time, DateTimeOffset At);

public sealed record RailDirectionView(
    int Id, string From, string To, IReadOnlyList<RailDepartureView> Next, IReadOnlyList<RailTripView> Trips, DateOnly NextDate);

public sealed record RailNoticeView(string Line, string Text, DateTimeOffset? UpdatedAt);

public sealed record RailStationResponse(RailStationView Station, IReadOnlyList<RailDirectionView> Directions, IReadOnlyList<RailNoticeView> Notices);

public sealed record RailDirectionSummary(int Id, string To, IReadOnlyList<RailDepartureView> Next, DateOnly NextDate);

public sealed class RailTimetables(MetroClient metro, TimeProvider clock, ILogger<RailTimetables> logger)
{
    private static readonly TimeSpan RetryAfterError = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<(int Station, int Direction), Entry> _cache = new();
    private readonly ConcurrentDictionary<(int Station, int Direction), Lazy<Task<IReadOnlyList<int>>>> _loading = new();

    public Task<IReadOnlyList<int>> GetAsync(int stationId, int directionId, CancellationToken ct)
    {
        var key = (stationId, directionId);
        if (_cache.TryGetValue(key, out var entry) && clock.GetUtcNow() < entry.RefreshAt) return Task.FromResult(entry.Minutes);
        return _loading.GetOrAdd(key, k => new Lazy<Task<IReadOnlyList<int>>>(() => LoadAsync(k))).Value.WaitAsync(ct);
    }

    private async Task<IReadOnlyList<int>> LoadAsync((int Station, int Direction) key)
    {
        var previous = _cache.GetValueOrDefault(key)?.Minutes ?? [];
        var now = clock.GetUtcNow();
        try
        {
            var times = await metro.GetTimeTableAsync(key.Station, key.Direction, CancellationToken.None);
            IReadOnlyList<int> minutes = times.Select(ServiceDay.MinuteOf).Distinct().Order().ToList();
            _cache[key] = new Entry(minutes, ServiceDay.Of(now).Ends);
            return minutes;
        }
        catch (Exception e) when (e is MetroException or HttpRequestException or JsonException or OperationCanceledException)
        {
            logger.LogWarning("Rail timetable {Station}/{Direction} failed: {Error}", key.Station, key.Direction, e.Message);
            _cache[key] = new Entry(previous, now + RetryAfterError);
            return previous;
        }
        finally
        {
            _loading.TryRemove(key, out _);
        }
    }

    private sealed record Entry(IReadOnlyList<int> Minutes, DateTimeOffset RefreshAt);
}

public sealed class RailBoard(RailCatalog catalog, RailTimetables timetables, TimeProvider clock)
{
    private const int NextShown = 3;

    public RailStationView View(RailStation station) =>
        new(station.Id, station.Name, station.LineId, station.Line, catalog.Line(station.LineId)?.Color ?? "#808080", station.Lat, station.Lng);

    public static RailLineView View(RailLine line) => new(line.Id, line.Name, line.Color, line.Description);

    public RailLineResponse? Line(int lineId)
    {
        if (catalog.Line(lineId) is not { } line) return null;
        var notices = catalog.Notices(lineId).Select(n => new RailNoticeView(line.Name, n.Text, n.UpdatedAt)).ToList();
        return new RailLineResponse(View(line), catalog.LineStations(lineId).Select(View).ToList(), notices);
    }

    public async Task<RailStationResponse?> StationAsync(int stationId, CancellationToken ct)
    {
        if (catalog.Station(stationId) is not { } station) return null;

        var day = ServiceDay.Of(clock.GetUtcNow());
        var directions = await DirectionsAsync(station, ct);
        var views = directions
            .Select(d => new RailDirectionView(d.Direction.Id, d.Direction.From, d.Direction.To, Next(d.Minutes, day),
                d.Minutes.Select(m => new RailTripView(Clock(m), day.At(m))).ToList(), day.Next.Date))
            .ToList();
        var notices = catalog.Notices(station.LineId).Select(n => new RailNoticeView(station.Line, n.Text, n.UpdatedAt)).ToList();
        return new RailStationResponse(View(station), views, notices);
    }

    public async Task<IReadOnlyList<RailDirectionSummary>> SummaryAsync(RailStation station, CancellationToken ct)
    {
        var day = ServiceDay.Of(clock.GetUtcNow());
        return (await DirectionsAsync(station, ct))
            .Select(d => new RailDirectionSummary(d.Direction.Id, d.Direction.To, Next(d.Minutes, day), day.Next.Date))
            .ToList();
    }

    private async Task<IReadOnlyList<(RailDirection Direction, IReadOnlyList<int> Minutes)>> DirectionsAsync(RailStation station, CancellationToken ct)
    {
        var directions = catalog.Directions(station.LineId).Where(d => !EndsAt(d, station)).ToList();
        var minutes = await Task.WhenAll(directions.Select(d => timetables.GetAsync(station.Id, d.Id, ct)));
        return directions.Zip(minutes).Where(x => x.Second.Count > 0).ToList();
    }

    private IReadOnlyList<RailDepartureView> Next(IReadOnlyList<int> minutes, ServiceDay day)
    {
        var now = day.MinuteOf(clock.GetUtcNow());
        var today = minutes.Where(m => m >= now).Take(NextShown).Select(m => new RailDepartureView(day.At(m), false));
        var tomorrow = minutes.Take(NextShown).Select(m => new RailDepartureView(day.Next.At(m), true));
        return today.Concat(tomorrow).Take(NextShown).ToList();
    }

    private static bool EndsAt(RailDirection direction, RailStation station)
    {
        var end = SearchText.Normalize(direction.To);
        var name = SearchText.Normalize(station.Name);
        return end.Length > 0 && (name.StartsWith(end) || end.StartsWith(name));
    }

    private static string Clock(int minute) => $"{minute / 60 % 24:00}:{minute % 60:00}";
}
