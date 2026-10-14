using BusTracker.Api.Shapes;

namespace BusTracker.Tests.Shapes;

public sealed class PolylineTests
{
    [Fact]
    public void Codec_MatchesGoogleReferenceExample()
    {
        LatLng[] points = [new(38.5, -120.2), new(40.7, -120.95), new(43.252, -126.453)];

        Assert.Equal("_p~iF~ps|U_ulLnnqC_mqNvxq`@", PolylineCodec.Encode(points));
        Assert.Equal(points, PolylineCodec.Decode("_p~iF~ps|U_ulLnnqC_mqNvxq`@"));
    }

    [Fact]
    public void Codec_RoundTripsIstanbulCoordinatesToAboutOneMeter()
    {
        LatLng[] points = [new(40.886697, 29.193002), new(40.8871234, 29.1925678), new(41.0849999, 29.0073091)];

        var decoded = PolylineCodec.Decode(PolylineCodec.Encode(points));

        Assert.All(points.Zip(decoded), pair =>
        {
            Assert.InRange(Math.Abs(pair.First.Lat - pair.Second.Lat), 0, 0.00001);
            Assert.InRange(Math.Abs(pair.First.Lng - pair.Second.Lng), 0, 0.00001);
        });
    }

    [Fact]
    public void Simplifier_DropsPointsOnAStraightLine()
    {
        var points = Enumerable.Range(0, 50).Select(i => new LatLng(41.0 + i * 0.0001, 29.0 + i * 0.0001)).ToList();

        var simplified = PolylineSimplifier.Simplify(points, 5);

        Assert.Equal([points[0], points[^1]], simplified);
    }

    [Fact]
    public void Simplifier_KeepsCornersBeyondTolerance()
    {
        LatLng[] points = [new(41.0, 29.0), new(41.0, 29.001), new(41.0, 29.002), new(41.001, 29.002), new(41.002, 29.002)];

        var simplified = PolylineSimplifier.Simplify(points, 5);

        Assert.Equal([points[0], points[2], points[4]], simplified);
    }

    [Fact]
    public void Simplifier_LeavesShortLinesAlone()
    {
        LatLng[] points = [new(41.0, 29.0), new(41.001, 29.001)];

        Assert.Same(points, PolylineSimplifier.Simplify(points, 5));
    }
}
