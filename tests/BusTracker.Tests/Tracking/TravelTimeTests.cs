using BusTracker.Api.Configuration;
using BusTracker.Api.Ibb;
using BusTracker.Api.Tracking;

namespace BusTracker.Tests.Tracking;

public sealed class TravelTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 8, 0, 0, TimeSpan.FromHours(3));
    private const string Line = "T1";
    private const double StepDegrees = 0.009;

    private readonly LineTracks _tracks = LineTracks.Build(
        Enumerable.Range(0, 5).Select(i => new RouteStop(Line, "G", "End", i + 1, $"S{i}", $"Stop {i}", 41 + i * StepDegrees, 29, "Test")).ToList(),
        [], 40);

    private RouteTrack Track => _tracks.Main("G")!;
    private double At(int stop) => Track.Stops[stop].AlongMeters;
    private double Hop(int from) => At(from + 1) - At(from);

    [Fact]
    public void Stops_AreAboutOneKilometreApart()
    {
        Assert.All(Enumerable.Range(0, 4), i => Assert.InRange(Hop(i), 999, 1002));
    }

    [Fact]
    public void WithoutHopData_UsesTheDefaultSpeed()
    {
        var hops = new HopTimes();
        var from = At(1) + 250;

        var estimate = TravelTime.Between(Track, from, At(3), null, hops, Now);

        var distance = At(3) - from;
        Assert.Equal((int)Math.Round(distance / 4.5), estimate.Seconds);
        Assert.False(estimate.HeavyTraffic);
    }

    [Fact]
    public void WithoutHopData_BlendsTheBusOwnSpeed()
    {
        var hops = new HopTimes();
        var from = At(1) + 250;

        var estimate = TravelTime.Between(Track, from, At(3), 3, hops, Now);

        var blended = 0.6 * 3 + 0.4 * 4.5;
        Assert.Equal(3.6, blended, 6);
        Assert.Equal((int)Math.Round((At(3) - from) / blended), estimate.Seconds);
    }

    [Fact]
    public void RecentHopTimes_AreAddedUpWithThePartOfTheCurrentHopStillToGo()
    {
        var hops = new HopTimes();
        hops.Record(new HopObservation(Track.RouteCode, 1, Now.AddMinutes(-5), 400));
        hops.Record(new HopObservation(Track.RouteCode, 2, Now.AddMinutes(-3), 300));
        var from = At(1) + 250;

        var estimate = TravelTime.Between(Track, from, At(3), null, hops, Now);

        var stillToGoOnHop1 = (Hop(1) - 250) / Hop(1);
        Assert.Equal((int)Math.Round(400 * stillToGoOnHop1 + 300), estimate.Seconds);
        Assert.InRange(estimate.Seconds, 599, 601);
    }

    [Fact]
    public void HopsWithoutRecentData_FallBackPieceByPiece()
    {
        var hops = new HopTimes();
        hops.Record(new HopObservation(Track.RouteCode, 2, Now.AddMinutes(-3), 300));
        var from = At(1) + 250;

        var estimate = TravelTime.Between(Track, from, At(3), 3, hops, Now);

        Assert.Equal((int)Math.Round((Hop(1) - 250) / 3.6 + 300), estimate.Seconds);
    }

    [Fact]
    public void HopTimesOlderThanHalfAnHour_AreIgnored()
    {
        var hops = new HopTimes();
        hops.Record(new HopObservation(Track.RouteCode, 1, Now.AddMinutes(-31), 900));

        Assert.Null(hops.RecentSeconds(Track.RouteCode, 1, Now));
        Assert.Equal((int)Math.Round(Hop(1) / 4.5), TravelTime.Between(Track, At(1), At(2), null, hops, Now).Seconds);
    }

    [Fact]
    public void RecentHopTime_IsTheMedian()
    {
        var hops = new HopTimes();
        foreach (var seconds in new[] { 100.0, 400, 130 })
            hops.Record(new HopObservation(Track.RouteCode, 1, Now.AddMinutes(-2), seconds));
        Assert.Equal(130, hops.RecentSeconds(Track.RouteCode, 1, Now));

        hops.Record(new HopObservation(Track.RouteCode, 1, Now.AddMinutes(-1), 200));
        Assert.Equal((130 + 200) / 2.0, hops.RecentSeconds(Track.RouteCode, 1, Now));
    }

    [Theory]
    [InlineData(600, true)]
    [InlineData(300, false)]
    public void HeavyTraffic_WhenBusesAheadAveragedUnderAboutEightKmPerHour(double secondsPerHop, bool heavy)
    {
        var hops = new HopTimes();
        hops.Record(new HopObservation(Track.RouteCode, 1, Now.AddMinutes(-4), secondsPerHop));
        hops.Record(new HopObservation(Track.RouteCode, 2, Now.AddMinutes(-4), secondsPerHop));

        var estimate = TravelTime.Between(Track, At(1), At(3), null, hops, Now);

        var speed = (Hop(1) + Hop(2)) / (2 * secondsPerHop);
        Assert.Equal(speed < 2.2, heavy);
        Assert.Equal(heavy, estimate.HeavyTraffic);
    }

    [Fact]
    public void HeavyTraffic_NeedsAtLeast400MetresOfEvidence()
    {
        var hops = new HopTimes();
        hops.Record(new HopObservation(Track.RouteCode, 1, Now.AddMinutes(-4), 900));

        Assert.False(TravelTime.Between(Track, At(2) - 300, At(3), null, hops, Now).HeavyTraffic);
        Assert.True(TravelTime.Between(Track, At(2) - 500, At(3), null, hops, Now).HeavyTraffic);
    }

    [Fact]
    public void Eta_SubtractsTimeSinceTheBusReported()
    {
        var hops = new HopTimes();
        hops.Record(new HopObservation(Track.RouteCode, 1, Now.AddMinutes(-5), 400));
        var bus = TrackedAt(At(1) + 1, Now.AddSeconds(-60));

        var estimate = Arrivals.Estimate(bus, Track, Track.Stops[2], hops, Now);

        var travel = 400 * (Hop(1) - 1) / Hop(1);
        Assert.Equal((int)Math.Round(travel - 60), estimate.Seconds);
    }

    [Fact]
    public void Eta_NeverGoesBelowZero()
    {
        var bus = TrackedAt(At(2) - 100, Now.AddMinutes(-2));

        Assert.Equal(0, Arrivals.Estimate(bus, Track, Track.Stops[2], new HopTimes(), Now).Seconds);
    }

    [Fact]
    public void Tracker_RecordsHopTimesFromInterpolatedStopCrossings()
    {
        var tracker = new LineTracker(new TrackingOptions());
        var observed = new List<HopObservation>();
        var start = Now.AddMinutes(-10);

        tracker.Update([BusAt(At(1) - 100, start)], _tracks, start, new TrackerEvents(observed.Add));
        tracker.Update([BusAt(At(2) + 100, start.AddSeconds(120))], _tracks, start.AddSeconds(120), new TrackerEvents(observed.Add));

        var hop = Assert.Single(observed);
        Assert.Equal(1, hop.FromStop);
        var span = At(2) + 100 - (At(1) - 100);
        Assert.Equal(120 * Hop(1) / span, hop.Seconds, 3);
        Assert.Equal(start.AddSeconds(120 * (At(2) - (At(1) - 100)) / span), hop.At, TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public void Tracker_DoesNotMeasureTheHopFromTheFirstStop()
    {
        var tracker = new LineTracker(new TrackingOptions());
        var observed = new List<HopObservation>();
        var start = Now.AddMinutes(-10);

        tracker.Update([BusAt(At(0) - 50, start)], _tracks, start, new TrackerEvents(observed.Add));
        tracker.Update([BusAt(At(1) + 50, start.AddSeconds(200))], _tracks, start.AddSeconds(200), new TrackerEvents(observed.Add));

        Assert.Empty(observed);
    }

    [Fact]
    public void Tracker_ReportsEveryStopCrossingWithItsInterpolatedTime()
    {
        var tracker = new LineTracker(new TrackingOptions());
        var crossings = new List<StopCrossing>();
        var start = Now.AddMinutes(-10);

        tracker.Update([BusAt(At(0) + 500, start)], _tracks, start, new TrackerEvents(Crossed: crossings.Add));
        tracker.Update([BusAt(At(2) + 500, start.AddSeconds(200))], _tracks, start.AddSeconds(200), new TrackerEvents(Crossed: crossings.Add));

        Assert.Equal([1, 2], crossings.Select(c => c.StopIndex));
        Assert.All(crossings, c => Assert.Equal("B1", c.DoorNo));
        var span = At(2) + 500 - (At(0) + 500);
        Assert.Equal(start.AddSeconds(200 * (At(1) - (At(0) + 500)) / span), crossings[0].At, TimeSpan.FromMilliseconds(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShownEta_FollowsTheChosenModel(bool useHopTimes)
    {
        var hops = new HopTimes();
        hops.Record(new HopObservation(Track.RouteCode, 1, Now.AddMinutes(-5), 600));
        hops.Record(new HopObservation(Track.RouteCode, 2, Now.AddMinutes(-5), 600));
        var bus = TrackedAt(At(1) + 1, Now);

        var arrival = Arrivals.ForStop("S3", "G", [bus], _tracks, new EtaContext(hops, Now, useHopTimes)).Approaching.Single();

        var both = Arrivals.EstimateBoth(bus, Track, Track.Stops[3], hops, Now);
        Assert.Equal(useHopTimes ? both.HopSeconds : both.SpeedSeconds, arrival.EtaSeconds);
        Assert.Equal(Arrivals.SpeedEstimate(At(3) - (At(1) + 1), null, Track.ScheduledSpeedMps), both.SpeedSeconds);
        Assert.True(arrival.HeavyTraffic);
    }

    private LiveBus BusAt(double along, DateTimeOffset at)
    {
        var point = Track.Geometry.PointAt(along);
        return new LiveBus("B1", point.Lat, point.Lng, Line, Track.RouteCode, "G", "D0", "End", at, "");
    }

    private TrackedBus TrackedAt(double along, DateTimeOffset reportedAt) =>
        new(BusAt(along, reportedAt), "t", true, along, 0, Track.Geometry.LengthMeters, null, null, null, 0, BusStatus.Ok, []);
}
