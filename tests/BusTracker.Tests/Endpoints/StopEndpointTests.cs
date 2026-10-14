using System.Net;
using System.Net.Http.Json;
using BusTracker.Api.Endpoints;
using BusTracker.Api.Stops;
using Microsoft.Extensions.DependencyInjection;

namespace BusTracker.Tests.Endpoints;

public sealed class StopEndpointTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Theory]
    [InlineData("şahinbey")]
    [InlineData("SAHINBEY")]
    [InlineData("sehit sah")]
    [InlineData("402011")]
    public async Task Search_FindsStopsIgnoringCaseAndTurkishLetters(string query)
    {
        var client = await factory.CreateUserClientAsync();

        var results = await client.GetFromJsonAsync<List<StopSearchResult>>($"/api/stops?q={Uri.EscapeDataString(query)}");

        var hit = Assert.Single(results!, r => r.Stop.Code == "402011");
        Assert.Equal("ŞEHİT ŞAHİNBEY", hit.Stop.Name);
        Assert.Contains("131Y", hit.Lines);
    }

    [Fact]
    public async Task Search_WithTooShortQuery_ReturnsNothing()
    {
        var client = await factory.CreateUserClientAsync();

        Assert.Empty((await client.GetFromJsonAsync<List<StopSearchResult>>("/api/stops?q=a"))!);
    }

    [Fact]
    public async Task StopDetails_ListsLinesWithDirectionAndPosition()
    {
        var client = await factory.CreateUserClientAsync();

        var details = await client.GetFromJsonAsync<StopDetails>("/api/stops/402011");

        Assert.Equal(5, details!.Lines.Select(l => l.LineCode).Distinct().Count());
        var line = Assert.Single(details.Lines, l => l.LineCode == "131Y");
        Assert.Equal(("G", 14), (line.Direction, line.Seq));
    }

    [Fact]
    public async Task Arrivals_MergeEveryLineAndReportLinesWithoutData()
    {
        var client = await factory.CreateUserClientAsync();

        var view = await client.GetFromJsonAsync<StopArrivalsView>("/api/stops/402011/arrivals");

        Assert.NotEmpty(view!.Arrivals);
        Assert.All(view.Arrivals, a => Assert.Equal(("131Y", "G"), (a.Line, a.Direction)));
        Assert.Contains(view.Arrivals, a => a.Bus.DoorNo == "C-114");
        Assert.Equal(view.Arrivals.OrderBy(a => a.Bus.EtaSeconds ?? int.MaxValue).ThenBy(a => a.Bus.DistanceMeters), view.Arrivals);
        var withData = Assert.Single(view.Lines, l => l.Data is not null);
        Assert.Equal("131Y", withData.Line);
        Assert.All(view.Lines.Where(l => l.Line != "131Y"), l => Assert.NotNull(l.Error));
    }

    [Fact]
    public async Task UnknownStop_IsNotFound()
    {
        var client = await factory.CreateUserClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/stops/999999/arrivals")).StatusCode);
    }

    [Fact]
    public async Task StopIndex_IsNotDownloadedAgainWithinTheRefreshPeriod()
    {
        await factory.SeedStopIndexAsync();

        var outcome = await factory.Services.GetRequiredService<StopIndexSync>().SyncAsync(CancellationToken.None);

        Assert.Equal(BusTracker.Api.Shapes.SyncOutcome.Unchanged, outcome);
    }
}

public sealed class SearchTextTests
{
    [Theory]
    [InlineData("Şehit Şahinbey", "SEHIT SAHINBEY")]
    [InlineData("ışık", "ISIK")]
    [InlineData("istanbul", "ISTANBUL")]
    [InlineData("  4.LEVENT   METRO ", "4 LEVENT METRO")]
    [InlineData("Çekmeköy/Ümraniye", "CEKMEKOY UMRANIYE")]
    public void Normalize_FoldsCaseAndTurkishLetters(string input, string expected)
    {
        Assert.Equal(expected, SearchText.Normalize(input));
    }
}
