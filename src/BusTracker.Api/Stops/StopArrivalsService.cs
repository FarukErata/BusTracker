using BusTracker.Api.Ibb;
using BusTracker.Api.Lines;
using BusTracker.Api.Tracing;
using BusTracker.Api.Tracking;

namespace BusTracker.Api.Stops;

public sealed record LineAtStop(StopLine Line, LiveResult? Live, string? Error, IReadOnlyList<Arrival> Arrivals, DateTimeOffset? RetryAt = null);

public sealed record StopArrivalsResult(StopInfo Stop, IReadOnlyList<LineAtStop> Lines);

public sealed class StopArrivalsService(StopIndexRepository stopIndex, LineDataService data, TimeProvider clock)
{
    public async Task<StopArrivalsResult?> GetAsync(string stopCode, IReadOnlySet<string>? wanted, CancellationToken ct)
    {
        if (await stopIndex.GetStopAsync(stopCode, ct) is not { } stop) return null;

        var lines = await stopIndex.GetLinesAtStopAsync(stopCode, ct);
        var fromMemory = new List<LineAtStop>();
        foreach (var line in lines)
            fromMemory.Add(await LoadAsync(stopCode, line, refreshRoster: false, ct));

        var results = await Task.WhenAll(MissingThenClosest(fromMemory).Select(known =>
            wanted is null || wanted.Contains(known.Line.LineCode) ? LoadAsync(stopCode, known.Line, refreshRoster: true, ct) : Task.FromResult(known)));

        return new StopArrivalsResult(stop, results.OrderBy(r => r.Line.LineCode).ThenBy(r => r.Line.Direction).ToList());
    }

    private IEnumerable<LineAtStop> MissingThenClosest(IEnumerable<LineAtStop> lines) =>
        lines
            .OrderBy(r => r.Live is null ? 0 : r.Arrivals.Count > 0 ? 1 : 2)
            .ThenBy(r => r.Arrivals.Select(a => a.EtaSeconds ?? int.MaxValue).DefaultIfEmpty(int.MaxValue).Min())
            .ThenBy(r => data.LastFetchedAt(r.Line.LineCode) ?? DateTimeOffset.MinValue);

    private async Task<LineAtStop> LoadAsync(string stopCode, StopLine line, bool refreshRoster, CancellationToken ct)
    {
        try
        {
            var tracks = await data.GetTracksAsync(line.LineCode, ct);
            var live = await data.GetLiveAsync(line.LineCode, ct, refreshRoster);
            var arrivals = Arrivals.ForStop(stopCode, line.Direction, live.Snapshot.Buses, tracks, data.Eta(clock.GetUtcNow())).Approaching;
            return new LineAtStop(line, live, null, arrivals);
        }
        catch (LineUnavailableException e)
        {
            return new LineAtStop(line, null, $"waiting for data ({e.Reason})", [], e.RetryAt);
        }
        catch (Exception e) when (e is IbbException or HttpRequestException || e is OperationCanceledException && !ct.IsCancellationRequested)
        {
            using (var span = Telemetry.Span("failed", $"{line.LineCode} {e.GetType().Name}")) span.Fail(e.Message);
            return new LineAtStop(line, null, "waiting for data (ibb-error)", [], DateTimeOffset.UtcNow.AddMinutes(1));
        }
    }
}
