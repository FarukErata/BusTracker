using System.Text.Json;
using BusTracker.Api.Ibb;

namespace BusTracker.Api.Timetables;

public sealed class AnnouncementService(IbbClient ibb, TimeProvider clock, ILogger<AnnouncementService> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private volatile IReadOnlyDictionary<string, IReadOnlyList<Announcement>> _byLine =
        new Dictionary<string, IReadOnlyList<Announcement>>();

    public IReadOnlyList<Announcement> For(string lineCode)
    {
        var now = clock.GetUtcNow();
        var day = ServiceDay.Of(now);
        return (_byLine.GetValueOrDefault(lineCode) ?? []).Where(a => Announcements.StillRelevant(a, day, now)).ToList();
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        var rows = await ibb.GetAnnouncementsAsync(ct);
        _byLine = rows
            .Select(Announcements.From)
            .GroupBy(a => a.Line)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Announcement>)g
                .OrderBy(a => a.Departure is { } d ? ServiceDay.MinuteOf(d) : -1)
                .ToList());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(await TryRefreshAsync(stoppingToken) ? Every : RetryAfter, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> TryRefreshAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RefreshAsync(stoppingToken);
            return true;
        }
        catch (Exception e) when (e is IbbException or HttpRequestException or JsonException
            || e is OperationCanceledException && !stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Announcements failed: {Error}", e.Message);
            return false;
        }
    }
}
