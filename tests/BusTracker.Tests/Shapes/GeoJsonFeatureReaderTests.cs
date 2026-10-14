using System.Text;
using BusTracker.Api.Shapes;

namespace BusTracker.Tests.Shapes;

public sealed class GeoJsonFeatureReaderTests
{
    private static async Task<List<string>> ReadRouteCodesAsync(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var codes = new List<string>();
        await foreach (var feature in GeoJsonFeatureReader.ReadFeaturesAsync(stream, CancellationToken.None))
        {
            using (feature)
                codes.Add(feature.RootElement.GetProperty("properties").GetProperty("GUZERGAH_KODU").GetString()!);
        }
        return codes;
    }

    [Fact]
    public async Task RealFixture_YieldsEveryFeature()
    {
        var codes = await ReadRouteCodesAsync(Fixture.Read("shapes-131Y.geojson"));

        Assert.Equal(["131Y_D_D0", "131Y_G_D0", "131Y_G_D1895"], codes);
    }

    [Fact]
    public async Task IgnoresObjectsOutsideTheFeaturesArray()
    {
        const string json = """
            {"type":"FeatureCollection","crs":{"type":"name","properties":{"GUZERGAH_KODU":"NOT_A_FEATURE"}},
             "features":[{"type":"Feature","properties":{"GUZERGAH_KODU":"A_G_D0"},"geometry":null}],
             "extra":[{"properties":{"GUZERGAH_KODU":"ALSO_NOT"}}]}
            """;

        Assert.Equal(["A_G_D0"], await ReadRouteCodesAsync(json));
    }

    [Fact]
    public async Task HandlesBracesAndEscapedQuotesInsideStrings()
    {
        const string json = """
            {"features":[
              {"properties":{"GUZERGAH_KODU":"B_G_D0","NOTE":"has } and ] and \" inside {"}},
              {"properties":{"GUZERGAH_KODU":"C_D_D0","NOTE":"back\\slash"}}
            ]}
            """;

        Assert.Equal(["B_G_D0", "C_D_D0"], await ReadRouteCodesAsync(json));
    }

    [Theory]
    [InlineData("""{"features":[{"properties":{"GUZERGAH_KODU":"E_G_D0"}},{"properties":{"GUZER""")]
    [InlineData("""{"features":[{"properties":{"GUZERGAH_KODU":"E_G_D0"}}""")]
    [InlineData("""{"features":[""")]
    public async Task TruncatedDocument_Throws(string json)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadRouteCodesAsync(json));
    }

    [Fact]
    public async Task HandlesFeatureListAppearingBeforeOtherKeys()
    {
        const string json = """{"features":[{"properties":{"GUZERGAH_KODU":"D_G_D0"}}],"name":"features"}""";

        Assert.Equal(["D_G_D0"], await ReadRouteCodesAsync(json));
    }
}
