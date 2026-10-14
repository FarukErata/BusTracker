using BusTracker.Api.Shapes;
using BusTracker.Api.Tracking;

namespace BusTracker.Tests.Tracking;

public sealed class RouteTrackTests
{
    private readonly LineTracks _tracks = Fixture131Y.Tracks();

    [Fact]
    public void Geometry_LengthMatchesPublishedLength()
    {
        var main = _tracks.Main("D")!;

        Assert.True(main.HasRoadGeometry);
        Assert.InRange(main.Geometry.LengthMeters, 27949 * 0.99, 27949 * 1.01);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1234)]
    [InlineData(15000)]
    [InlineData(27000)]
    public void Geometry_PointAtThenProject_RoundTrips(double along)
    {
        var geometry = _tracks.Main("D")!.Geometry;

        var projection = geometry.Project(geometry.PointAt(along));

        Assert.InRange(projection.AlongMeters, along - 1, along + 1);
        Assert.InRange(projection.OffsetMeters, 0, 0.5);
    }

    [Fact]
    public void Geometry_WindowRestrictsWhereAPointCanLand()
    {
        var geometry = _tracks.Main("D")!.Geometry;
        var point = geometry.PointAt(20000);

        var inWindow = geometry.Project(point, minAlong: 19000, maxAlong: 21000);
        var outsideWindow = geometry.Project(point, minAlong: 0, maxAlong: 5000);

        Assert.InRange(inWindow.AlongMeters, 19999, 20001);
        Assert.InRange(outsideWindow.AlongMeters, 0, 5000);
        Assert.True(outsideWindow.OffsetMeters > 100);
    }

    [Theory]
    [InlineData("D", 58)]
    [InlineData("G", 56)]
    public void MainTrack_PlacesEveryStopInOrderCloseToTheRoad(string direction, int stopCount)
    {
        var stops = _tracks.Main(direction)!.Stops;

        Assert.Equal(stopCount, stops.Count);
        Assert.Equal(Enumerable.Range(1, stopCount), stops.Select(s => s.Seq));
        Assert.All(stops.Zip(stops.Skip(1)), pair => Assert.True(pair.Second.AlongMeters > pair.First.AlongMeters));
        Assert.All(stops, s => Assert.InRange(s.OffsetMeters, 0, 25));
    }

    [Fact]
    public void VariantTrack_UsesItsOwnGeometryAndTheStopsOnIt()
    {
        var variant = _tracks.For("131Y_G_D1895", "G")!;

        Assert.Equal("131Y_G_D1895", variant.RouteCode);
        Assert.False(variant.IsMainVariant);
        Assert.InRange(variant.Stops.Count, 10, 56);
        Assert.All(variant.Stops.Zip(variant.Stops.Skip(1)), pair => Assert.True(pair.Second.Seq > pair.First.Seq));
    }

    [Fact]
    public void UnknownVariant_FallsBackToTheMainTrackOfItsDirection()
    {
        Assert.Same(_tracks.Main("D"), _tracks.For("131Y_D_D9999", "D"));
    }

    [Fact]
    public void WithoutShapes_FallsBackToStraightLinesBetweenStops()
    {
        var tracks = LineTracks.Build(Fixture131Y.Stops, [], 40);

        var main = tracks.Main("D")!;
        Assert.False(main.HasRoadGeometry);
        Assert.Equal(58, main.Stops.Count);
        Assert.Equal(RouteTrack.DefaultSpeedMps, main.ScheduledSpeedMps);
    }

    [Fact]
    public void StopsUntil_CountsStopsAfterTheBusUpToAndIncludingTheTarget()
    {
        var main = _tracks.Main("D")!;
        var stop10 = main.Stops[9];
        var justAfterStop5 = main.Stops[4].AlongMeters + 50;

        Assert.Equal(5, main.StopsUntil(justAfterStop5, stop10.AlongMeters));
        Assert.Equal(0, main.StopsUntil(stop10.AlongMeters - 10, stop10.AlongMeters));
    }

    [Fact]
    public void ScheduledSpeed_ComesFromPublishedLengthAndDuration()
    {
        var shape = Fixture131Y.Shapes.Single(s => s.RouteCode == "131Y_D_D0");

        Assert.Equal(shape.LengthMeters / shape.DurationSeconds, _tracks.Main("D")!.ScheduledSpeedMps, 6);
    }

    [Fact]
    public void PointAt_ClampsToTheRoute()
    {
        var geometry = _tracks.Main("D")!.Geometry;

        Assert.Equal(geometry.Points[0], geometry.PointAt(-100));
        Assert.Equal(geometry.Points[^1], geometry.PointAt(geometry.LengthMeters + 100));
        Assert.IsType<LatLng>(geometry.PointAt(geometry.LengthMeters / 2));
    }
}
