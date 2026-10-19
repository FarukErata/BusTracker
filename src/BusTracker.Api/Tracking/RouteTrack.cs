using BusTracker.Api.Ibb;
using BusTracker.Api.Shapes;

namespace BusTracker.Api.Tracking;

public sealed record PlacedStop(int Index, string StopCode, int Seq, string Name, double AlongMeters, double OffsetMeters, double Lat, double Lng);

public sealed class RouteTrack
{
    private const double StopReachedMeters = 30;

    private RouteTrack(
        string routeCode, string direction, string variant, RouteGeometry geometry,
        IReadOnlyList<PlacedStop> stops, double scheduledSpeedMps, bool hasRoadGeometry)
    {
        RouteCode = routeCode;
        Direction = direction;
        Variant = variant;
        Geometry = geometry;
        Stops = stops;
        ScheduledSpeedMps = scheduledSpeedMps;
        HasRoadGeometry = hasRoadGeometry;
    }

    public string RouteCode { get; }
    public string Direction { get; }
    public string Variant { get; }
    public RouteGeometry Geometry { get; }
    public IReadOnlyList<PlacedStop> Stops { get; }
    public double ScheduledSpeedMps { get; }
    public bool HasRoadGeometry { get; }
    public bool IsMainVariant => Variant == "D0";

    public static RouteTrack FromShape(RouteShape shape, IReadOnlyList<RouteStop> directionStops, double stopOnRouteMeters)
    {
        var geometry = new RouteGeometry(shape.Points);
        var speed = shape.DurationSeconds > 0 ? shape.LengthMeters / shape.DurationSeconds : DefaultSpeedMps;
        return new RouteTrack(shape.RouteCode, shape.Direction, shape.Variant, geometry,
            PlaceStops(geometry, directionStops, stopOnRouteMeters), speed, hasRoadGeometry: true);
    }

    public static RouteTrack FromStops(string routeCode, string direction, IReadOnlyList<RouteStop> directionStops)
    {
        var geometry = new RouteGeometry(directionStops.Select(s => new LatLng(s.Lat, s.Lng)).ToList());
        var (_, variant) = IbbParsers.SplitRouteCode(routeCode);
        return new RouteTrack(routeCode, direction, variant, geometry,
            PlaceStops(geometry, directionStops, double.MaxValue), DefaultSpeedMps, hasRoadGeometry: false);
    }

    public const double DefaultSpeedMps = 5.5;

    public PlacedStop? FindStop(string stopCode) => Stops.FirstOrDefault(s => s.StopCode == stopCode);

    public PlacedStop? LastStopReached(double alongMeters) =>
        Stops.LastOrDefault(s => s.AlongMeters <= alongMeters + StopReachedMeters);

    public PlacedStop? NextStop(double alongMeters) =>
        Stops.FirstOrDefault(s => s.AlongMeters > alongMeters + StopReachedMeters);

    public int StopsUntil(double fromAlong, double stopAlong) =>
        Stops.Count(s => s.AlongMeters > fromAlong + StopReachedMeters && s.AlongMeters <= stopAlong);

    public IEnumerable<PlacedStop> StopsPassedBetween(double fromAlong, double toAlong) =>
        Stops.Where(s => s.AlongMeters > fromAlong && s.AlongMeters <= toAlong);

    private static List<PlacedStop> PlaceStops(RouteGeometry geometry, IReadOnlyList<RouteStop> stops, double maxOffset)
    {
        var placed = new List<PlacedStop>();
        var minAlong = double.NegativeInfinity;
        foreach (var stop in stops.OrderBy(s => s.Seq))
        {
            var projection = geometry.Project(new LatLng(stop.Lat, stop.Lng), minAlong);
            if (projection.OffsetMeters > maxOffset) continue;

            placed.Add(new PlacedStop(placed.Count, stop.StopCode, stop.Seq, stop.Name, projection.AlongMeters, projection.OffsetMeters, stop.Lat, stop.Lng));
            minAlong = projection.AlongMeters - StopReachedMeters;
        }
        return placed;
    }
}

public sealed class LineTracks
{
    private readonly Dictionary<string, RouteTrack> _byRouteCode;
    private readonly Dictionary<string, RouteTrack> _mainByDirection;

    private LineTracks(Dictionary<string, RouteTrack> byRouteCode, Dictionary<string, RouteTrack> mainByDirection)
    {
        _byRouteCode = byRouteCode;
        _mainByDirection = mainByDirection;
        Mains = mainByDirection.Values.ToList();
    }

    public IReadOnlyList<RouteTrack> Mains { get; }

    public static readonly LineTracks Empty = new([], []);

    public IEnumerable<RouteTrack> All => _byRouteCode.Values;

    public static LineTracks Build(IReadOnlyList<RouteStop> stops, IReadOnlyList<RouteShape> shapes, double stopOnRouteMeters)
    {
        var stopsByDirection = stops.GroupBy(s => s.Direction).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Seq).ToList());
        var byRouteCode = shapes
            .Where(s => stopsByDirection.ContainsKey(s.Direction))
            .Select(s => RouteTrack.FromShape(s, stopsByDirection[s.Direction], stopOnRouteMeters))
            .ToDictionary(t => t.RouteCode);

        var mainByDirection = stopsByDirection.ToDictionary(
            d => d.Key,
            d => byRouteCode.Values.FirstOrDefault(t => t.Direction == d.Key && t.IsMainVariant)
                 ?? RouteTrack.FromStops($"{d.Value[0].LineCode}_{d.Key}_D0", d.Key, d.Value));

        return new LineTracks(byRouteCode, mainByDirection);
    }

    public RouteTrack? For(string routeCode, string direction) =>
        _byRouteCode.GetValueOrDefault(routeCode) ?? Main(direction);

    public RouteTrack? Main(string direction) => _mainByDirection.GetValueOrDefault(direction);
}
