using System.Net;
using System.Net.Http.Json;
using BusTracker.Api.Endpoints;

namespace BusTracker.Tests.Endpoints;

public sealed class HealthEndpointTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
        Assert.True(body.UptimeSeconds >= 0);
        Assert.Equal(0, body.RouteShapes.Count);
    }

    [Fact]
    public async Task Shapes_ForLineWithoutImport_ReturnsEmptyList()
    {
        var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<LineShapesResponse>("/api/lines/131y/shapes");

        Assert.NotNull(body);
        Assert.Equal("131Y", body.Line);
        Assert.Empty(body.Shapes);
    }
}
