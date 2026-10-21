using BusTracker.Api.Ibb;
using BusTracker.Api.Timetables;
using BusTracker.Tests.Tracking;

namespace BusTracker.Tests.Timetables;

public sealed class TimetableTests
{
    private static readonly DateTimeOffset Now = Fixture131Y.SnapshotTime;

    private static readonly Timetable Table =
        new(IbbParsers.ParsePlannedTrips(SoapEnvelope.ReadResult("GetPlanlananSeferSaati_json", Fixture.Read("timetable-131Y.xml"))));

    private static readonly IReadOnlyList<Announcement> Notices =
        IbbParsers.ParseAnnouncements(SoapEnvelope.ReadResult("GetDuyurular_json", Fixture.Read("announcements.xml")))
            .Select(Announcements.From)
            .ToList();

    private static readonly Dictionary<string, string> FirstStops = new() { ["G"] = "YEŞİLTEPE SON DURAK", ["D"] = "KARTAL" };

    [Fact]
    public void Timetable_KeepsAfterMidnightTripsAtTheEndOfTheServiceDay()
    {
        var weekday = Table.Day("G", "I");

        Assert.Equal("06:00", weekday[0].Clock);
        Assert.Equal("00:20", weekday[^1].Clock);
        Assert.Equal(24 * 60 + 20, weekday[^1].Minute);
        Assert.Contains(weekday, t => t is { RouteCode: "131Y_G_D1895", Variant: "D1895", Clock: "13:30" });
        Assert.Equal(["D", "G"], Table.Directions);
    }

    [Theory]
    [InlineData("2026-10-15T01:30:00+03:00", "2026-10-14", "I", false)]
    [InlineData("2026-10-17T10:00:00+03:00", "2026-10-17", "C", false)]
    [InlineData("2026-10-18T02:59:00+03:00", "2026-10-17", "C", false)]
    [InlineData("2026-10-18T03:00:00+03:00", "2026-10-18", "P", false)]
    [InlineData("2026-10-29T09:00:00+03:00", "2026-10-29", "P", true)]
    public void ServiceDay_StartsAtThreeAndUsesSundayTimesOnHolidays(string now, string date, string dayType, bool holiday)
    {
        var day = ServiceDay.Of(DateTimeOffset.Parse(now));

        Assert.Equal((DateOnly.Parse(date), dayType, holiday), (day.Date, day.DayType, day.Holiday));
    }

    [Fact]
    public void Announcements_SplitCancelledTripsFromNoticesAndDropDuplicates()
    {
        Assert.Equal(5, Notices.Count);
        var trip = Notices[0];
        Assert.True(trip.CancelledTrip);
        Assert.Equal(("131Y", "YESILTEPE SONDURAK", new TimeOnly(13, 25)), (trip.Line, trip.FromStop, trip.Departure!.Value));
        Assert.Equal("Çesitli nedenlerle yapilamayacaktir.", trip.Reason);
        Assert.Equal("04:06", trip.PostedAt);
        var notice = Assert.Single(Notices, a => !a.CancelledTrip);
        Assert.Equal("YOL CALISMASI NEDENIYLE ARAÇLARIMIZ KARTAL YÖNÜNDE GÜZERGAH DISINA ÇIKACAKTIR.", notice.Message);
    }

    [Theory]
    [InlineData("13:25", "G")]
    [InlineData("13:40", "D")]
    [InlineData("12:00", "D")]
    public void Announcements_FindTheirDirectionByTimeThenByFirstStop(string time, string direction)
    {
        var notice = Notices.First(a => a.Departure == TimeOnly.Parse(time));

        Assert.Equal(direction, Announcements.DirectionOf(notice, Table, ServiceDay.Of(Now), FirstStops));
    }

    [Fact]
    public void Announcements_FromAnUnknownStopAtASharedTime_StayOnTheLine()
    {
        var notice = Notices.First(a => a.Departure == new TimeOnly(13, 40)) with { FromStop = "BILINMEYEN" };

        Assert.Null(Announcements.DirectionOf(notice, Table, ServiceDay.Of(Now), FirstStops));
    }

    [Theory]
    [InlineData("12:00", false)]
    [InlineData("12:55", true)]
    [InlineData("13:40", true)]
    public void CancelledTrips_DisappearFifteenMinutesAfterTheirDeparture(string time, bool shown)
    {
        var notice = Notices[0] with { Departure = TimeOnly.Parse(time) };

        Assert.Equal(shown, Announcements.StillRelevant(notice, ServiceDay.Of(Now), Now));
    }

    [Fact]
    public void Plan_GivesWaitingBusesTheNextDeparturesInOrder()
    {
        var plan = Plan();

        var (view, waiting) = plan.For("D", null, [new WaitingBus("A", "131Y_D_D0"), new WaitingBus("B", "131Y_D_D0")], Now);

        Assert.Equal(At(13, 5), waiting["A"].Departs);
        Assert.Equal(At(13, 15), waiting["B"].Departs);
        Assert.Equal("KARTAL", view!.FirstStop);
        Assert.Equal("I", view.DayType);
        Assert.Equal([(At(13, 40), true), (At(14, 0), false), (At(6, 10).AddDays(1), false)],
            view.Next.Select(t => (t.Departs, t.Cancelled)));
    }

    [Fact]
    public void Plan_GivesWaitingBusesTripsOfTheirOwnVariantAndSkipsCancelledOnes()
    {
        var (view, waiting) = Plan().For("G", null, [new WaitingBus("A", "131Y_G_D0"), new WaitingBus("B", "131Y_G_D0")], At(13, 20));

        Assert.Equal(At(13, 40), waiting["A"].Departs);
        Assert.Equal(At(13, 55), waiting["B"].Departs);
        Assert.Equal([("13:25", true), ("13:30", false), ("14:10", false)],
            view!.Next.Select(t => (t.Departs.ToString("HH:mm"), t.Cancelled)));
    }

    [Fact]
    public void Plan_EstimatesArrivalAtTheStopFromTheScheduledSpeed()
    {
        var (view, _) = Plan().For("G", "402011", [], Now);

        var first = view!.Next[0];
        Assert.Equal(At(13, 10), first.Departs);
        Assert.NotNull(first.AtStop);
        Assert.InRange(first.AtStop!.Value - first.Departs, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(40));
        Assert.True(view.Next[1].Cancelled);
    }

    [Fact]
    public void Plan_LeavesOutTripsThatAlreadyPassedTheStop()
    {
        var (early, _) = Plan().For("G", "402011", [], At(13, 0));
        var passedAt = early!.Next[0].AtStop!.Value;

        var (later, _) = Plan().For("G", "402011", [], passedAt.AddMinutes(3));

        Assert.DoesNotContain(later!.Next, t => t.Departs == early.Next[0].Departs);
    }

    private static LinePlan Plan()
    {
        var day = ServiceDay.Of(Now);
        var notices = Notices.Where(a => a.Line == "131Y" && Announcements.StillRelevant(a, day, Now))
            .Select(a => (a, Announcements.DirectionOf(a, Table, day, FirstStops)))
            .ToList();
        return new LinePlan(Table, Fixture131Y.Tracks(), day, notices);
    }

    private static DateTimeOffset At(int hour, int minute) => new(2026, 10, 14, hour, minute, 0, TimeSpan.FromHours(3));
}
