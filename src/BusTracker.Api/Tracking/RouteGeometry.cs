using BusTracker.Api.Shapes;

namespace BusTracker.Api.Tracking;

public readonly record struct Projection(double AlongMeters, double OffsetMeters);

public sealed class RouteGeometry
{
    private const double MetersPerDegree = 111_320;

    private readonly LatLng[] _points;
    private readonly double[] _cumulative;
    private readonly double _cosLat;

    public RouteGeometry(IReadOnlyList<LatLng> points)
    {
        if (points.Count < 2) throw new ArgumentException("A route needs at least two points", nameof(points));

        _points = [.. points];
        _cosLat = Math.Cos(_points[0].Lat * Math.PI / 180);
        _cumulative = new double[_points.Length];
        for (var i = 1; i < _points.Length; i++)
            _cumulative[i] = _cumulative[i - 1] + SegmentLength(i);
    }

    public double LengthMeters => _cumulative[^1];

    public IReadOnlyList<LatLng> Points => _points;

    public Projection Project(LatLng point, double minAlong = double.NegativeInfinity, double maxAlong = double.PositiveInfinity)
    {
        var windowed = ProjectWithin(point, minAlong, maxAlong);
        return windowed ?? ProjectWithin(point, double.NegativeInfinity, double.PositiveInfinity)!.Value;
    }

    public LatLng PointAt(double alongMeters)
    {
        var along = Math.Clamp(alongMeters, 0, LengthMeters);
        var i = Array.BinarySearch(_cumulative, along);
        if (i >= 0) return _points[i];

        var end = ~i;
        var start = end - 1;
        var t = (along - _cumulative[start]) / (_cumulative[end] - _cumulative[start]);
        return new LatLng(
            _points[start].Lat + t * (_points[end].Lat - _points[start].Lat),
            _points[start].Lng + t * (_points[end].Lng - _points[start].Lng));
    }

    private Projection? ProjectWithin(LatLng point, double minAlong, double maxAlong)
    {
        Projection? best = null;
        var (px, py) = ToMeters(point);
        for (var i = 1; i < _points.Length; i++)
        {
            var candidate = ProjectOnSegment(i, px, py);
            if (candidate.AlongMeters < minAlong || candidate.AlongMeters > maxAlong) continue;
            if (best is null || candidate.OffsetMeters < best.Value.OffsetMeters) best = candidate;
        }
        return best;
    }

    private Projection ProjectOnSegment(int end, double px, double py)
    {
        var (ax, ay) = ToMeters(_points[end - 1]);
        var (bx, by) = ToMeters(_points[end]);
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1);
        var offset = Math.Sqrt(Math.Pow(px - (ax + t * dx), 2) + Math.Pow(py - (ay + t * dy), 2));
        return new Projection(_cumulative[end - 1] + t * (_cumulative[end] - _cumulative[end - 1]), offset);
    }

    private double SegmentLength(int end)
    {
        var (ax, ay) = ToMeters(_points[end - 1]);
        var (bx, by) = ToMeters(_points[end]);
        return Math.Sqrt(Math.Pow(bx - ax, 2) + Math.Pow(by - ay, 2));
    }

    private (double X, double Y) ToMeters(LatLng p) => (p.Lng * MetersPerDegree * _cosLat, p.Lat * MetersPerDegree);
}
