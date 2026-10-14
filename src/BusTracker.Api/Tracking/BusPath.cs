using BusTracker.Api.Shapes;

namespace BusTracker.Api.Tracking;

public sealed record BusPath(
    TrackedBus Bus,
    RouteTrack Track,
    PlacedStop Stop,
    bool Passed,
    double DistanceMeters,
    IReadOnlyList<LatLng> Points,
    IReadOnlyList<PlacedStop> RemainingStops);

public enum BusPathProblem { None, BusNotFound, StopNotOnRoute }

public static class BusPaths
{
    private const double AtStopMeters = 30;

    public static (BusPath? Path, BusPathProblem Problem) Between(
        string doorNo, string stopCode, IReadOnlyList<TrackedBus> buses, LineTracks tracks)
    {
        var bus = buses.FirstOrDefault(b => b.Bus.DoorNo == doorNo && b.AlongMeters is not null);
        if (bus is null) return (null, BusPathProblem.BusNotFound);

        var track = tracks.For(bus.Bus.RouteCode, bus.Bus.Direction);
        if (track?.FindStop(stopCode) is not { } stop) return (null, BusPathProblem.StopNotOnRoute);

        var from = bus.AlongMeters!.Value;
        var passed = stop.AlongMeters < from - AtStopMeters;
        return (new BusPath(
            bus,
            track,
            stop,
            passed,
            passed ? 0 : Math.Max(0, stop.AlongMeters - from),
            passed ? [] : track.Geometry.Slice(from, stop.AlongMeters),
            passed ? [] : track.Stops.Where(s => s.AlongMeters > from + AtStopMeters && s.AlongMeters <= stop.AlongMeters).ToList()), BusPathProblem.None);
    }
}
