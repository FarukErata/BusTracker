using BusTracker.Api.Ibb;

namespace BusTracker.Tests.Ibb;

public sealed class IbbParsingTests
{
    [Fact]
    public void LiveBuses_AreParsedWithTypedValues()
    {
        var result = SoapEnvelope.ReadResult("GetHatOtoKonum_json", Fixture.Read("live-131Y.xml"));

        var buses = IbbParsers.ParseLiveBuses(result);

        Assert.Equal(11, buses.Count);
        var first = buses[0];
        Assert.Equal("O2031", first.DoorNo);
        Assert.Equal(40.893783, first.Lat, 6);
        Assert.Equal(29.19782, first.Lng, 6);
        Assert.Equal("131Y", first.LineCode);
        Assert.Equal("131Y_D_D0", first.RouteCode);
        Assert.Equal("D", first.Direction);
        Assert.Equal("D0", first.Variant);
        Assert.True(first.IsMainVariant);
        Assert.Equal("YEŞİLTEPE SON DURAK", first.Destination);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 13, 6, 47, TimeSpan.FromHours(3)), first.ReportedAt);
        Assert.Equal("227501", first.NearestStopCode);
    }

    [Fact]
    public void LiveBuses_KeepAlternateVariants()
    {
        var buses = IbbParsers.ParseLiveBuses(SoapEnvelope.ReadResult("GetHatOtoKonum_json", Fixture.Read("live-131Y.xml")));

        var alternate = Assert.Single(buses, b => !b.IsMainVariant);
        Assert.Equal("A-1541", alternate.DoorNo);
        Assert.Equal("G", alternate.Direction);
        Assert.Equal("D1895", alternate.Variant);
    }

    [Fact]
    public void RouteStops_AreOrderedPerDirection()
    {
        var result = SoapEnvelope.ReadResult("DurakDetay_GYY_wYonAdi", Fixture.Read("route-stops-131Y.xml"));

        var stops = IbbParsers.ParseRouteStops(result);

        var outbound = stops.Where(s => s.Direction == "G").ToList();
        var inbound = stops.Where(s => s.Direction == "D").ToList();
        Assert.Equal(56, outbound.Count);
        Assert.Equal(58, inbound.Count);
        Assert.Equal(Enumerable.Range(1, 58), inbound.Select(s => s.Seq));

        var firstInbound = inbound[0];
        Assert.Equal("405191", firstInbound.StopCode);
        Assert.Equal("KARTAL", firstInbound.Name);
        Assert.Equal("YEŞİLTEPE SON DURAK", firstInbound.DirectionName);
        Assert.Equal(40.886697, firstInbound.Lat, 6);
        Assert.Equal(29.193002, firstInbound.Lng, 6);
        Assert.Equal("KARTAL", outbound[^1].Name);
    }

    [Fact]
    public void RateLimitFault_ThrowsRateLimitedException()
    {
        Assert.Throws<IbbRateLimitedException>(() =>
            SoapEnvelope.ReadResult("GetHatOtoKonum_json", Fixture.Read("fault-rate-limit.xml")));
    }

    [Fact]
    public void UnknownLineFault_ThrowsFaultWithServerMessage()
    {
        var e = Assert.Throws<IbbFaultException>(() =>
            SoapEnvelope.ReadResult("GetHatOtoKonum_json", Fixture.Read("fault-unknown-line.xml")));

        Assert.Contains("(400) Bad Request", e.Message);
    }

    [Fact]
    public void NonXmlResponse_ThrowsIbbException()
    {
        Assert.Throws<IbbException>(() => SoapEnvelope.ReadResult("GetHatOtoKonum_json", "<html>bad gateway"));
    }

    [Fact]
    public void Envelope_EscapesParameterValues()
    {
        var xml = SoapEnvelope.Build("GetHatOtoKonum_json", new Dictionary<string, string> { ["HatKodu"] = "<500T&>" });

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml);
        Assert.Contains("<HatKodu>&lt;500T&amp;&gt;</HatKodu>", xml);
    }

    [Theory]
    [InlineData("500T_G_D0", "G", "D0")]
    [InlineData("19F_D_D5167", "D", "D5167")]
    [InlineData("broken", "?", "?")]
    public void RouteCode_IsSplitIntoDirectionAndVariant(string routeCode, string direction, string variant)
    {
        Assert.Equal((direction, variant), IbbParsers.SplitRouteCode(routeCode));
    }
}
