using System.Net;
using System.Net.Http.Json;
using BusTracker.Api.Endpoints;

namespace BusTracker.Tests.Endpoints;

public sealed class HealthEndpointTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Health_IsPublic()
    {
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("ok", body!.Status);
        Assert.Equal(0, body.RouteShapes.Count);
    }

    [Fact]
    public async Task Shapes_ForLineWithoutImport_ReturnsEmptyList()
    {
        var client = await factory.CreateUserClientAsync();

        var body = await client.GetFromJsonAsync<LineShapesResponse>("/api/lines/131y/shapes");

        Assert.Equal("131Y", body!.Line);
        Assert.Empty(body.Shapes);
    }
}
