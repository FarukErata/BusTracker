using BusTracker.Api.Configuration;
using BusTracker.Api.Ibb;

namespace BusTracker.Api.Lines;

public static class BusStatus
{
    public const string Ok = "ok";
    public const string AtTerminal = "at-terminal";
    public const string Stale = "stale";
    public const string OffRoute = "off-route";
    public const string AlternateRoute = "alternate-route";
}

public sealed record LiveBusView(
    string DoorNo,
    double Lat,
    double Lng,
    string Direction,
    string Variant,
    string RouteCode,
    string Destination,
    DateTimeOffset ReportedAt,
    int AgeSeconds,
    string NearestStopCode,
    int? NearestStopIndex,
    int? DistanceToNearestMeters,
    string Status);

public static class LiveLineView
{
    public static IReadOnlyList<LiveBusView> Build(
        IReadOnlyList<LiveBus> buses, IReadOnlyList<RouteStop> stops, TrackingOptions tracking, DateTimeOffset now)
    {
        var stopsByDirection = stops
            .GroupBy(s => s.Direction)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Seq).ToList());

        return buses
            .Select(bus => ToView(bus, stopsByDirection.GetValueOrDefault(bus.Direction) ?? [], tracking, now))
            .OrderBy(v => v.Direction)
            .ThenByDescending(v => v.NearestStopIndex ?? -1)
            .ToList();
    }

    private static LiveBusView ToView(LiveBus bus, List<RouteStop> directionStops, TrackingOptions tracking, DateTimeOffset now)
    {
        var index = directionStops.FindIndex(s => s.StopCode == bus.NearestStopCode);
        var nearest = index >= 0 ? directionStops[index] : null;
        var distance = nearest is null ? (int?)null : (int)Geo.DistanceMeters(bus.Lat, bus.Lng, nearest.Lat, nearest.Lng);
        var ageSeconds = (int)Math.Max(0, (now - bus.ReportedAt).TotalSeconds);

        return new LiveBusView(
            bus.DoorNo, bus.Lat, bus.Lng, bus.Direction, bus.Variant, bus.RouteCode, bus.Destination,
            bus.ReportedAt, ageSeconds, bus.NearestStopCode,
            index >= 0 ? index : null, distance,
            Classify(bus, index, distance, ageSeconds, tracking));
    }

    private static string Classify(LiveBus bus, int index, int? distance, int ageSeconds, TrackingOptions tracking)
    {
        if (ageSeconds > tracking.StaleAfterSeconds) return BusStatus.Stale;
        if (index < 0) return bus.IsMainVariant ? BusStatus.OffRoute : BusStatus.AlternateRoute;
        if (distance > tracking.OffRouteMeters) return BusStatus.OffRoute;
        if (index == 0) return BusStatus.AtTerminal;
        return BusStatus.Ok;
    }
}
