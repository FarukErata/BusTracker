using BusTracker.Api.Tracking;
using static BusTracker.Tests.Tracking.Fixture131Y;

namespace BusTracker.Tests.Tracking;

public sealed class LineTrackerTests
{
    private readonly LineTracks _tracks = Tracks();
    private readonly LineTracker _tracker = new(Options);

    private TrackedBus Single(string doorNo, DateTimeOffset? now = null) =>
        _tracker.Update(Buses, _tracks, now ?? SnapshotTime).Single(b => b.Bus.DoorNo == doorNo);

    [Fact]
    public void BusOnRoute_IsPlacedBetweenTheRightStops()
    {
        var bus = Single("O2031");

        Assert.Equal(BusStatus.Ok, bus.Status);
        Assert.InRange(bus.OffsetMeters!.Value, 0, 60);
        Assert.NotNull(bus.LastStop);
        Assert.NotNull(bus.NextStop);
        Assert.Equal(bus.LastStop!.Seq + 1, bus.NextStop!.Seq);
        Assert.InRange(bus.LastStop.Seq, 4, 5);
        Assert.InRange(bus.AlongMeters!.Value, bus.LastStop.AlongMeters - 30, bus.NextStop.AlongMeters + 30);
    }

    [Fact]
    public void BusesAtTheFirstStop_AreWaitingAtTheTerminal()
    {
        var tracked = _tracker.Update(Buses, _tracks, SnapshotTime);

        Assert.Equal(BusStatus.AtTerminal, tracked.Single(b => b.Bus.DoorNo == "O1042").Status);
        Assert.Equal(BusStatus.AtTerminal, tracked.Single(b => b.Bus.DoorNo == "O3103").Status);
    }

    [Fact]
    public void AlternateVariantBus_IsTrackedOnItsOwnRoute()
    {
        var bus = Single("A-1541");

        Assert.True(bus.OnOwnVariant);
        Assert.Equal("131Y_G_D1895", bus.Bus.RouteCode);
        Assert.NotNull(bus.AlongMeters);
    }

    [Fact]
    public void MovingForward_ReportsPassedStopsAndSpeed()
    {
        var main = _tracks.Main("D")!;
        var first = _tracker.Update([Bus("O2057")], _tracks, SnapshotTime).Single();
        var moved = MovedAlong(Bus("O2057"), main, first.AlongMeters!.Value + 800, TimeSpan.FromSeconds(60));

        var second = _tracker.Update([moved], _tracks, SnapshotTime.AddSeconds(60)).Single();

        var expected = main.StopsPassedBetween(first.AlongMeters!.Value, second.AlongMeters!.Value).Select(s => s.Seq);
        Assert.NotEmpty(second.PassedSinceLastUpdate);
        Assert.Equal(expected, second.PassedSinceLastUpdate.Select(s => s.Seq));
        Assert.Equal(first.TripId, second.TripId);
        Assert.InRange(second.SpeedMps!.Value, 13.0, 13.7);
    }

    [Fact]
    public void SameReportTimeAgain_ChangesNothing()
    {
        _tracker.Update([Bus("O2057")], _tracks, SnapshotTime);

        var again = _tracker.Update([Bus("O2057")], _tracks, SnapshotTime.AddSeconds(30)).Single();

        Assert.Empty(again.PassedSinceLastUpdate);
        Assert.Null(again.SpeedMps);
    }

    [Fact]
    public void JumpingBackToTheStart_StartsANewTrip()
    {
        var main = _tracks.Main("D")!;
        var first = _tracker.Update([Bus("C-115")], _tracks, SnapshotTime).Single();
        var restarted = MovedAlong(Bus("C-115"), main, 20, TimeSpan.FromMinutes(5));

        var second = _tracker.Update([restarted], _tracks, SnapshotTime.AddMinutes(5)).Single();

        Assert.NotEqual(first.TripId, second.TripId);
        Assert.Empty(second.PassedSinceLastUpdate);
        Assert.Equal(BusStatus.AtTerminal, second.Status);
    }

    [Fact]
    public void ChangingDirection_StartsANewTrip()
    {
        var first = _tracker.Update([Bus("O2057")], _tracks, SnapshotTime).Single();
        var other = _tracks.Main("G")!;
        var turned = MovedAlong(Bus("O2057") with { RouteCode = "131Y_G_D0", Direction = "G" }, other, 1000, TimeSpan.FromMinutes(1));

        var second = _tracker.Update([turned], _tracks, SnapshotTime.AddMinutes(1)).Single();

        Assert.NotEqual(first.TripId, second.TripId);
        Assert.Empty(second.PassedSinceLastUpdate);
    }

    [Fact]
    public void OldReports_AreStale()
    {
        Assert.Equal(BusStatus.Stale, Single("O2031", SnapshotTime.AddMinutes(10)).Status);
    }

    [Fact]
    public void BusFarFromItsRoad_IsOffRoute()
    {
        var far = Bus("O2031") with { Lat = Bus("O2031").Lat + 0.01 };

        var bus = _tracker.Update([far], _tracks, SnapshotTime).Single();

        Assert.Equal(BusStatus.OffRoute, bus.Status);
        Assert.True(bus.OffsetMeters > Options.OffRouteMeters);
    }

    [Fact]
    public void BusAtTheLastStop_HasArrived()
    {
        var main = _tracks.Main("D")!;
        var atEnd = MovedAlong(Bus("O2031"), main, main.Stops[^1].AlongMeters, TimeSpan.Zero);

        var bus = new LineTracker(Options).Update([atEnd], _tracks, SnapshotTime).Single();

        Assert.Equal(BusStatus.Arrived, bus.Status);
    }

    [Fact]
    public void LineWithoutTracks_MarksBusesAsNoRoute()
    {
        var bus = _tracker.Update([Bus("O2031")], LineTracks.Empty, SnapshotTime).Single();

        Assert.Equal(BusStatus.NoRoute, bus.Status);
        Assert.Null(bus.AlongMeters);
    }
}
