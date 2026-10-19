using System.Text.Json.Nodes;
using BusTracker.Api.Tracing;
using Microsoft.Extensions.DependencyInjection;

namespace BusTracker.Tests.Tracing;

public sealed class RequestLogTests
{
    [Fact]
    public async Task ApiRequest_IsLoggedWithItsWaits()
    {
        using var factory = new TestAppFactory();
        await factory.SeedStopIndexAsync();
        var client = await factory.CreateUserClientAsync();

        await client.GetAsync("/api/stops/402011/arrivals");
        await client.GetAsync("/health");

        var records = await ReadAsync(factory);
        var arrivals = records.Single(r => (string?)r["path"] == "/api/stops/402011/arrivals");
        Assert.Equal("request", (string?)arrivals["kind"]);
        Assert.Equal("/api/stops/{code}/arrivals", (string?)arrivals["route"]);
        Assert.Equal(200, (int?)arrivals["status"]);
        Assert.NotNull(arrivals["user"]);
        Assert.NotNull(arrivals["waits"]?["ibb"]);
        Assert.NotNull(arrivals["waits"]?["db"]);
        Assert.Contains(arrivals["spans"]!.AsArray(), s => (string?)s?["name"] == "ibb" && (string?)s["detail"] == "GetFiloAracKonum_json");
        Assert.Contains(arrivals["spans"]!.AsArray(), s => (string?)s?["name"] == "wait" && ((string?)s["detail"])!.StartsWith("roster "));
        Assert.DoesNotContain(records, r => (string?)r["path"] == "/health");

        var roster = records.First(r => (string?)r["name"] == "roster refresh");
        Assert.Equal("background", (string?)roster["kind"]);
        Assert.Contains(roster["spans"]!.AsArray(), s => (string?)s?["name"] == "ibb" && ((string?)s["detail"])!.StartsWith("GetHatOtoKonum_json"));
    }

    private static async Task<IReadOnlyList<JsonNode>> ReadAsync(TestAppFactory factory)
    {
        var log = factory.Services.GetRequiredService<RequestLog>();
        await log.WhenWrittenAsync(TimeSpan.FromSeconds(5));
        return Directory.EnumerateFiles(log.Folder, "requests-*.jsonl")
            .SelectMany(File.ReadAllLines)
            .Select(line => JsonNode.Parse(line)!)
            .ToList();
    }
}
