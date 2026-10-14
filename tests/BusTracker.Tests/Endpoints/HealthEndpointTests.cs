using System.Net;
using System.Net.Http.Json;
using BusTracker.Api.Endpoints;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusTracker.Tests.Endpoints;

public sealed class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
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
    }
}
