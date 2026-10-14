using System.Text;

namespace BusTracker.Api.Shapes;

public readonly record struct LatLng(double Lat, double Lng);

public static class PolylineSimplifier
{
    private const double MetersPerDegree = 111_320;

    public static IReadOnlyList<LatLng> Simplify(IReadOnlyList<LatLng> points, double toleranceMeters)
    {
        if (points.Count < 3 || toleranceMeters <= 0) return points;

        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        var cosLat = Math.Cos(points[0].Lat * Math.PI / 180);
        var ranges = new Stack<(int Start, int End)>();
        ranges.Push((0, points.Count - 1));

        while (ranges.Count > 0)
        {
            var (start, end) = ranges.Pop();
            var (farthest, distance) = FarthestPoint(points, start, end, cosLat);
            if (distance <= toleranceMeters) continue;

            keep[farthest] = true;
            ranges.Push((start, farthest));
            ranges.Push((farthest, end));
        }

        return points.Where((_, i) => keep[i]).ToList();
    }

    private static (int Index, double Distance) FarthestPoint(IReadOnlyList<LatLng> points, int start, int end, double cosLat)
    {
        var best = (Index: start, Distance: 0.0);
        for (var i = start + 1; i < end; i++)
        {
            var d = DistanceToSegmentMeters(points[i], points[start], points[end], cosLat);
            if (d > best.Distance) best = (i, d);
        }
        return best;
    }

    private static double DistanceToSegmentMeters(LatLng p, LatLng a, LatLng b, double cosLat)
    {
        var (px, py) = ToMeters(p, cosLat);
        var (ax, ay) = ToMeters(a, cosLat);
        var (bx, by) = ToMeters(b, cosLat);
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1);
        return Math.Sqrt(Math.Pow(px - (ax + t * dx), 2) + Math.Pow(py - (ay + t * dy), 2));
    }

    private static (double X, double Y) ToMeters(LatLng p, double cosLat) =>
        (p.Lng * MetersPerDegree * cosLat, p.Lat * MetersPerDegree);
}

public static class PolylineCodec
{
    private const double Factor = 1e5;

    public static string Encode(IEnumerable<LatLng> points)
    {
        var sb = new StringBuilder();
        long lastLat = 0, lastLng = 0;
        foreach (var p in points)
        {
            var lat = (long)Math.Round(p.Lat * Factor);
            var lng = (long)Math.Round(p.Lng * Factor);
            AppendValue(sb, lat - lastLat);
            AppendValue(sb, lng - lastLng);
            (lastLat, lastLng) = (lat, lng);
        }
        return sb.ToString();
    }

    public static IReadOnlyList<LatLng> Decode(string encoded)
    {
        var points = new List<LatLng>();
        var index = 0;
        long lat = 0, lng = 0;
        while (index < encoded.Length)
        {
            lat += ReadValue(encoded, ref index);
            lng += ReadValue(encoded, ref index);
            points.Add(new LatLng(lat / Factor, lng / Factor));
        }
        return points;
    }

    private static void AppendValue(StringBuilder sb, long value)
    {
        var v = value < 0 ? ~(value << 1) : value << 1;
        while (v >= 0x20)
        {
            sb.Append((char)((0x20 | (v & 0x1f)) + 63));
            v >>= 5;
        }
        sb.Append((char)(v + 63));
    }

    private static long ReadValue(string encoded, ref int index)
    {
        long result = 0;
        var shift = 0;
        int b;
        do
        {
            b = encoded[index++] - 63;
            result |= (long)(b & 0x1f) << shift;
            shift += 5;
        } while (b >= 0x20);
        return (result & 1) != 0 ? ~(result >> 1) : result >> 1;
    }
}
