using System.Collections.Concurrent;

namespace BusTracker.Api.Tracking;

public sealed record HopObservation(string RouteCode, int FromStop, DateTimeOffset At, double Seconds);

public sealed record StopCrossing(string RouteCode, string DoorNo, int StopIndex, DateTimeOffset At);

public sealed record TrackerEvents(Action<HopObservation>? Hop = null, Action<StopCrossing>? Crossed = null);

public sealed class HopTimes
{
    public static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(30);
    private const int KeptPerHop = 8;

    private readonly ConcurrentDictionary<(string RouteCode, int FromStop), Queue<HopObservation>> _byHop = new();

    public event Action<HopObservation>? Recorded;

    public void Record(HopObservation observation)
    {
        Add(observation);
        Recorded?.Invoke(observation);
    }

    public void Restore(IEnumerable<HopObservation> observations)
    {
        foreach (var observation in observations.OrderBy(o => o.At)) Add(observation);
    }

    public double? RecentSeconds(string routeCode, int fromStop, DateTimeOffset now)
    {
        if (!_byHop.TryGetValue((routeCode, fromStop), out var queue)) return null;

        double[] recent;
        lock (queue) recent = queue.Where(o => now - o.At <= RecentWindow && o.At <= now).Select(o => o.Seconds).Order().ToArray();
        if (recent.Length == 0) return null;
        var middle = recent.Length / 2;
        return recent.Length % 2 == 1 ? recent[middle] : (recent[middle - 1] + recent[middle]) / 2;
    }

    private void Add(HopObservation observation)
    {
        var queue = _byHop.GetOrAdd((observation.RouteCode, observation.FromStop), _ => new Queue<HopObservation>());
        lock (queue)
        {
            queue.Enqueue(observation);
            while (queue.Count > KeptPerHop) queue.Dequeue();
        }
    }
}
