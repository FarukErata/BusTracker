using BusTracker.Api.Configuration;
using BusTracker.Api.Ibb;
using BusTracker.Api.Lines;

namespace BusTracker.Tests.Lines;

public sealed class LiveLineViewTests
{
    private static readonly DateTimeOffset FixtureTime = new(2026, 10, 14, 13, 7, 0, TimeSpan.FromHours(3));

    private static readonly IReadOnlyList<RouteStop> Stops =
        IbbParsers.ParseRouteStops(SoapEnvelope.ReadResult("DurakDetay_GYY_wYonAdi", Fixture.Read("route-stops-131Y.xml")));

    private static readonly IReadOnlyList<LiveBus> Buses =
        IbbParsers.ParseLiveBuses(SoapEnvelope.ReadResult("GetHatOtoKonum_json", Fixture.Read("live-131Y.xml")));

    private static IReadOnlyList<LiveBusView> Build(DateTimeOffset now) =>
        LiveLineView.Build(Buses, Stops, new TrackingOptions(), now);

    [Theory]
    [InlineData("O2031", 4)]
    [InlineData("C-114", 7)]
    [InlineData("O1025", 37)]
    [InlineData("C-115", 50)]
    public void NearestStop_IsLocatedInItsOwnDirection(string doorNo, int expectedIndex)
    {
        var bus = Build(FixtureTime).Single(b => b.DoorNo == doorNo);

        Assert.Equal(expectedIndex, bus.NearestStopIndex);
        Assert.NotNull(bus.DistanceToNearestMeters);
    }

    [Fact]
    public void BusesAtFirstStop_AreWaitingAtTerminal()
    {
        var views = Build(FixtureTime);

        Assert.Equal(BusStatus.AtTerminal, views.Single(b => b.DoorNo == "O1042").Status);
        Assert.Equal(BusStatus.AtTerminal, views.Single(b => b.DoorNo == "O3103").Status);
    }

    [Fact]
    public void FreshBusesOnRoute_AreOk()
    {
        var bus = Build(FixtureTime).Single(b => b.DoorNo == "O2031");

        Assert.Equal(BusStatus.Ok, bus.Status);
        Assert.InRange(bus.AgeSeconds, 0, 60);
    }

    [Fact]
    public void OldReports_AreStale()
    {
        var views = Build(FixtureTime.AddMinutes(10));

        Assert.All(views, v => Assert.Equal(BusStatus.Stale, v.Status));
    }

    [Fact]
    public void BusFarFromItsNearestStop_IsOffRoute()
    {
        var farAway = Buses[0] with { Lat = Buses[0].Lat + 0.05 };

        var view = Assert.Single(LiveLineView.Build([farAway], Stops, new TrackingOptions(), FixtureTime));

        Assert.Equal(BusStatus.OffRoute, view.Status);
    }

    [Fact]
    public void AlternateVariantWithUnknownStop_IsAlternateRoute()
    {
        var alternate = Buses[0] with { Variant = "D6536", NearestStopCode = "999999" };

        var view = Assert.Single(LiveLineView.Build([alternate], Stops, new TrackingOptions(), FixtureTime));

        Assert.Equal(BusStatus.AlternateRoute, view.Status);
        Assert.Null(view.NearestStopIndex);
    }
}
