using BusTracker.Api.Ibb;

namespace BusTracker.Tests.Ibb;

public sealed class FleetParsingTests
{
    private static readonly TimeSpan Istanbul = TimeSpan.FromHours(3);

    [Fact]
    public void Fleet_IsParsedAndRowsWithoutPositionAreSkipped()
    {
        var now = new DateTimeOffset(2026, 10, 14, 13, 7, 0, Istanbul);
        var result = SoapEnvelope.ReadResult("GetFiloAracKonum_json", Fixture.Read("fleet-sample.xml"));

        var fleet = IbbParsers.ParseFleet(result, now);

        Assert.Equal(["O2031", "C-114", "A-001"], fleet.Select(v => v.DoorNo));
        var first = fleet[0];
        Assert.Equal(40.894012, first.Lat, 6);
        Assert.Equal(29.19751, first.Lng, 6);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 13, 6, 59, Istanbul), first.ReportedAt);
    }

    [Theory]
    [InlineData("2026-10-14 00:02:00", "23:59:30", "2026-10-13 23:59:30")]
    [InlineData("2026-10-14 13:07:00", "13:09:00", "2026-10-14 13:09:00")]
    [InlineData("2026-10-14 13:07:00", "00:36:34", "2026-10-14 00:36:34")]
    public void FleetTime_TakesTheDateOfTheMostRecentMatchingClockTime(string now, string clock, string expected)
    {
        var at = IbbParsers.InferIstanbulTime(clock, IbbParsers.ParseIstanbulTime(now).ToUniversalTime());

        Assert.Equal(IbbParsers.ParseIstanbulTime(expected), at);
    }
}
