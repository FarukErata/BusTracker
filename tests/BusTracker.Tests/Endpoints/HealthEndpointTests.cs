using System.Net;
using System.Net.Http.Json;
using BusTracker.Api.Endpoints;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task Health_ShowsTheCommitTheImageWasBuiltFrom()
    {
        using var built = factory.WithWebHostBuilder(b => b.UseSetting("BUSTRACKER_COMMIT", "abc1234"));

        var body = await built.CreateClient().GetFromJsonAsync<HealthResponse>("/health");

        Assert.Equal("abc1234", body!.Commit);
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

public sealed class FallbackTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task UnknownApiPath_IsNotServedTheAppPage()
    {
        var client = await factory.CreateUserClientAsync();

        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/api/does-not-exist")).StatusCode);
    }
}

public sealed class AppShellTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/stops/402011")]
    [InlineData("/lines/131Y")]
    public async Task AppRoutes_AreServedWithoutAKey(string path)
    {
        var webRoot = factory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().WebRootPath;
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><title>Buses</title>");

        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Buses</title>", await response.Content.ReadAsStringAsync());
    }
}
