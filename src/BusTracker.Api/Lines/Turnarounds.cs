using BusTracker.Api.Configuration;
using BusTracker.Api.Ibb;
using BusTracker.Api.Shapes;
using BusTracker.Api.Tracking;

namespace BusTracker.Api.Lines;

public sealed record TurnaroundResult(IReadOnlyList<LiveBus> Flipped, IReadOnlySet<string> Lost);

public static class Turnarounds
{
    private const double TurnBackMeters = 100;
    private const double NewTripStartMeters = 1500;

    public static TurnaroundResult Find(
        IReadOnlyList<TrackedBus> previous, IReadOnlyList<TrackedBus> current, LineTracks tracks, TrackingOptions options)
    {
        var before = previous.ToDictionary(b => b.Bus.DoorNo);
        var flipped = new List<LiveBus>();
        var lost = new HashSet<string>();
        foreach (var bus in current)
        {
            if (!before.TryGetValue(bus.Bus.DoorNo, out var old) || !IsTurning(old, bus)) continue;

            if (Opposite(bus.Bus, tracks, options) is { } opposite) flipped.Add(opposite);
            else if (old.TripId != bus.TripId) lost.Add(bus.Bus.DoorNo);
        }
        return new TurnaroundResult(flipped, lost);
    }

    private static bool IsTurning(TrackedBus old, TrackedBus now)
    {
        if (old.Bus.RouteCode != now.Bus.RouteCode || now.Bus.ReportedAt <= old.Bus.ReportedAt) return false;
        return old.TripId != now.TripId || old.Status == BusStatus.Arrived && old.AlongMeters - now.AlongMeters > TurnBackMeters;
    }

    private static LiveBus? Opposite(LiveBus bus, LineTracks tracks, TrackingOptions options)
    {
        if (OppositeDirection(bus.Direction) is not { } direction || tracks.Main(direction) is not { } track) return null;
        if (!StartsNear(track, new LatLng(bus.Lat, bus.Lng), options)) return null;

        return bus with
        {
            RouteCode = track.RouteCode,
            Direction = direction,
            Variant = track.Variant,
            Destination = track.Stops.LastOrDefault()?.Name ?? bus.Destination,
            NearestStopCode = "",
        };
    }

    private static string? OppositeDirection(string direction) => direction switch { "G" => "D", "D" => "G", _ => null };

    private static bool StartsNear(RouteTrack track, LatLng point, TrackingOptions options)
    {
        var projection = track.Geometry.Project(point);
        return projection.OffsetMeters <= LineTracker.OffRouteLimit(options, track) && projection.AlongMeters <= NewTripStartMeters;
    }
}
