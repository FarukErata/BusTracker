using System.Text;
using BusTracker.Api.Lines;
using BusTracker.Api.Shapes;

namespace BusTracker.Tests.Shapes;

public sealed class RouteShapeParserTests
{
    private static async Task<List<(RouteShape Simplified, RouteShape Original)>> ParseFixtureAsync(double tolerance)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Fixture.Read("shapes-131Y.geojson")));
        var shapes = new List<(RouteShape, RouteShape)>();
        await foreach (var feature in GeoJsonFeatureReader.ReadFeaturesAsync(stream, CancellationToken.None))
        {
            using (feature)
                shapes.Add((RouteShapeParser.Parse(feature, tolerance)!, RouteShapeParser.Parse(feature, 0)!));
        }
        return shapes;
    }

    [Fact]
    public async Task Parses_PropertiesWithCommaDecimals()
    {
        var (shape, original) = (await ParseFixtureAsync(5))[0];

        Assert.Equal("131Y_D_D0", shape.RouteCode);
        Assert.Equal("131Y", shape.LineCode);
        Assert.Equal("D", shape.Direction);
        Assert.Equal("D0", shape.Variant);
        Assert.True(shape.IsMainVariant);
        Assert.Equal(27949.44, shape.LengthMeters, 2);
        Assert.Equal(4057.18, shape.DurationSeconds, 2);
        Assert.Equal(930, original.Points.Count);
        Assert.InRange(shape.Points.Count, 2, original.Points.Count / 2);
    }

    [Fact]
    public async Task Geometry_StartsAtTheFirstStopOfItsDirection()
    {
        var (shape, _) = (await ParseFixtureAsync(5))[0];
        var kartalStop = new LatLng(40.886697, 29.193002);

        var start = shape.Points[0];
        Assert.InRange(Geo.DistanceMeters(start.Lat, start.Lng, kartalStop.Lat, kartalStop.Lng), 0, 30);
    }

    [Fact]
    public async Task Simplification_NeverMovesTheLineMoreThanTheTolerance()
    {
        foreach (var (simplified, original) in await ParseFixtureAsync(5))
        {
            var worst = original.Points.Max(p => DistanceToPolyline(p, simplified.Points));
            Assert.InRange(worst, 0, 5.05);
        }
    }

    private static double DistanceToPolyline(LatLng p, IReadOnlyList<LatLng> line)
    {
        var cosLat = Math.Cos(p.Lat * Math.PI / 180);
        const double m = 111_320;
        double best = double.MaxValue;
        for (var i = 1; i < line.Count; i++)
        {
            double ax = line[i - 1].Lng * m * cosLat, ay = line[i - 1].Lat * m;
            double bx = line[i].Lng * m * cosLat, by = line[i].Lat * m;
            double px = p.Lng * m * cosLat, py = p.Lat * m;
            double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            var t = l2 == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / l2, 0, 1);
            best = Math.Min(best, Math.Sqrt(Math.Pow(px - (ax + t * dx), 2) + Math.Pow(py - (ay + t * dy), 2)));
        }
        return best;
    }
}
