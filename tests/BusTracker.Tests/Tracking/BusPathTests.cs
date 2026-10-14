using BusTracker.Api.Shapes;
using BusTracker.Api.Tracking;
using static BusTracker.Tests.Tracking.Fixture131Y;

namespace BusTracker.Tests.Tracking;

public sealed class BusPathTests
{
    private readonly LineTracks _tracks = Tracks();
    private readonly IReadOnlyList<TrackedBus> _tracked;

    public BusPathTests() => _tracked = new LineTracker(Options).Update(Buses, _tracks, SnapshotTime);

    private string StopCode(string direction, int seq) => _tracks.Main(direction)!.Stops.Single(s => s.Seq == seq).StopCode;

    [Fact]
    public void Slice_StartsAndEndsAtTheExactPositions()
    {
        var geometry = _tracks.Main("D")!.Geometry;

        var slice = geometry.Slice(1000, 3000);

        Assert.True(slice.Count > 2);
        Assert.Equal(geometry.PointAt(1000), slice[0]);
        Assert.Equal(geometry.PointAt(3000), slice[^1]);
        var length = slice.Zip(slice.Skip(1)).Sum(p => BusTracker.Api.Lines.Geo.DistanceMeters(p.First.Lat, p.First.Lng, p.Second.Lat, p.Second.Lng));
        Assert.InRange(length, 1990, 2010);
    }

    [Fact]
    public void PathToAStopAhead_FollowsTheRoadAndListsTheStopsInBetween()
    {
        var target = StopCode("D", 45);

        var (path, problem) = BusPaths.Between("O1025", target, _tracked, _tracks);

        Assert.Equal(BusPathProblem.None, problem);
        Assert.False(path!.Passed);
        Assert.Equal(target, path.RemainingStops[^1].StopCode);
        Assert.Equal(Enumerable.Range(path.RemainingStops[0].Seq, path.RemainingStops.Count), path.RemainingStops.Select(s => s.Seq));
        var start = new LatLng(path.Bus.Bus.Lat, path.Bus.Bus.Lng);
        Assert.InRange(BusTracker.Api.Lines.Geo.DistanceMeters(start.Lat, start.Lng, path.Points[0].Lat, path.Points[0].Lng), 0, 60);
        Assert.InRange(path.DistanceMeters, 1000, 10000);
    }

    [Fact]
    public void StopAlreadyPassed_ReturnsAnEmptyPath()
    {
        var (path, problem) = BusPaths.Between("C-115", StopCode("D", 45), _tracked, _tracks);

        Assert.Equal(BusPathProblem.None, problem);
        Assert.True(path!.Passed);
        Assert.Empty(path.Points);
        Assert.Empty(path.RemainingStops);
    }

    [Fact]
    public void UnknownBusOrStop_ReportsTheProblem()
    {
        Assert.Equal(BusPathProblem.BusNotFound, BusPaths.Between("NOPE", StopCode("D", 45), _tracked, _tracks).Problem);
        Assert.Equal(BusPathProblem.StopNotOnRoute, BusPaths.Between("O1025", "999999", _tracked, _tracks).Problem);
    }
}
