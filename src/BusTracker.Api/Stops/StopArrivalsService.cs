using BusTracker.Api.Ibb;
using BusTracker.Api.Lines;
using BusTracker.Api.Tracking;

namespace BusTracker.Api.Stops;

public sealed record LineAtStop(StopLine Line, LiveResult? Live, string? Error, IReadOnlyList<Arrival> Arrivals);

public sealed record StopArrivalsResult(StopInfo Stop, IReadOnlyList<LineAtStop> Lines);

public sealed class StopArrivalsService(StopIndexRepository stopIndex, LineDataService data)
{
    public async Task<StopArrivalsResult?> GetAsync(string stopCode, CancellationToken ct)
    {
        if (await stopIndex.GetStopAsync(stopCode, ct) is not { } stop) return null;

        var lines = await stopIndex.GetLinesAtStopAsync(stopCode, ct);
        var fromMemory = new List<LineAtStop>();
        foreach (var line in lines)
            fromMemory.Add(await LoadAsync(stopCode, line, refreshRoster: false, ct));

        var results = new List<LineAtStop>();
        foreach (var known in ClosestFirst(fromMemory))
            results.Add(await LoadAsync(stopCode, known.Line, refreshRoster: true, ct));

        return new StopArrivalsResult(stop, results.OrderBy(r => r.Line.LineCode).ThenBy(r => r.Line.Direction).ToList());
    }

    private IEnumerable<LineAtStop> ClosestFirst(IEnumerable<LineAtStop> lines) =>
        lines
            .OrderBy(r => r.Arrivals.Count > 0 ? 0 : r.Live is null ? 1 : 2)
            .ThenBy(r => r.Arrivals.Select(a => a.EtaSeconds ?? int.MaxValue).DefaultIfEmpty(int.MaxValue).Min())
            .ThenBy(r => data.LastFetchedAt(r.Line.LineCode) ?? DateTimeOffset.MinValue);

    private async Task<LineAtStop> LoadAsync(string stopCode, StopLine line, bool refreshRoster, CancellationToken ct)
    {
        try
        {
            var tracks = await data.GetTracksAsync(line.LineCode, ct);
            var live = await data.GetLiveAsync(line.LineCode, ct, refreshRoster);
            var arrivals = Arrivals.ForStop(stopCode, line.Direction, live.Snapshot.Buses, tracks).Approaching;
            return new LineAtStop(line, live, null, arrivals);
        }
        catch (LineUnavailableException e)
        {
            return new LineAtStop(line, null, $"waiting for data ({e.Reason})", []);
        }
        catch (IbbException e)
        {
            return new LineAtStop(line, null, e.Message, []);
        }
    }
}
