using BusTracker.Api.Tracking;
using static BusTracker.Tests.Tracking.Fixture131Y;

namespace BusTracker.Tests.Tracking;

public sealed class ArrivalsTests
{
    private readonly LineTracks _tracks = Tracks();
    private readonly IReadOnlyList<TrackedBus> _tracked;
    private readonly PlacedStop _stop45;

    public ArrivalsTests()
    {
        _tracked = new LineTracker(Options).Update(Buses, _tracks, SnapshotTime);
        _stop45 = _tracks.Main("D")!.Stops.Single(s => s.Seq == 45);
    }

    private StopArrivals For45() => Arrivals.ForStop(_stop45.StopCode, "D", _tracked, _tracks);

    [Fact]
    public void ApproachingBuses_AreOrderedByDistanceWithStopsAwayAndEta()
    {
        var approaching = For45().Approaching;

        Assert.Equal(approaching.OrderBy(a => a.DistanceMeters), approaching);
        var nearest = approaching[0];
        Assert.Equal("O1025", nearest.Bus.Bus.DoorNo);
        Assert.InRange(nearest.StopsAway, 6, 8);
        Assert.True(nearest.DistanceMeters > 0);
        Assert.Equal(Arrivals.EstimateSeconds(nearest.DistanceMeters, null, _tracks.Main("D")!.ScheduledSpeedMps), nearest.EtaSeconds);
    }

    [Fact]
    public void BusesWaitingAtTheTerminal_HaveNoEta()
    {
        var waiting = For45().Approaching.Where(a => a.Bus.Status == BusStatus.AtTerminal).ToList();

        Assert.NotEmpty(waiting);
        Assert.All(waiting, a =>
        {
            Assert.True(a.NotDeparted);
            Assert.Null(a.EtaSeconds);
        });
    }

    [Fact]
    public void BusBeyondTheStop_IsListedAsRecentlyPassed()
    {
        var arrivals = For45();

        Assert.DoesNotContain(arrivals.Approaching, a => a.Bus.Bus.DoorNo == "C-115");
        var passed = Assert.Single(arrivals.RecentlyPassed, p => p.Bus.Bus.DoorNo == "C-115");
        Assert.True(passed.MetersPast > 0);
    }

    [Fact]
    public void BusesInTheOtherDirection_AreIgnored()
    {
        var arrivals = For45();

        Assert.All(arrivals.Approaching, a => Assert.Equal("D", a.Bus.Bus.Direction));
        Assert.All(arrivals.RecentlyPassed, p => Assert.Equal("D", p.Bus.Bus.Direction));
    }

    [Fact]
    public void Eta_BlendsObservedAndScheduledSpeed()
    {
        Assert.Equal(1000, Arrivals.EstimateSeconds(5000, null, 5));
        Assert.Equal(500, Arrivals.EstimateSeconds(5000, 10 * 1.0 / 0.6 - 5 * 0.4 / 0.6, 5));
        Assert.Equal(0, Arrivals.EstimateSeconds(20, 10, 5));
        Assert.Equal(2500, Arrivals.EstimateSeconds(5000, 0, 1));
    }
}
