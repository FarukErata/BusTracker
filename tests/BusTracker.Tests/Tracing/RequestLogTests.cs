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
        Assert.Contains(records, IsLoggedArrivals);
        Assert.DoesNotContain(records, r => (string?)r["path"] == "/health");

        var roster = records.First(r => (string?)r["name"] == "roster refresh");
        Assert.Equal("background", (string?)roster["kind"]);
        Assert.Contains(roster["spans"]!.AsArray(), s => (string?)s?["name"] == "ibb" && ((string?)s["detail"])!.StartsWith("GetHatOtoKonum_json"));
    }

    private static bool IsLoggedArrivals(JsonNode record) =>
        (string?)record["path"] == "/api/stops/402011/arrivals"
        && (string?)record["kind"] == "request"
        && (string?)record["route"] == "/api/stops/{code}/arrivals"
        && (int?)record["status"] == 200
        && record["user"] is not null
        && record["waits"]?["ibb"] is not null
        && record["waits"]?["db"] is not null
        && record["spans"]!.AsArray().Any(s => (string?)s?["name"] == "ibb" && (string?)s["detail"] == "GetFiloAracKonum_json")
        && record["spans"]!.AsArray().Any(s => (string?)s?["name"] == "wait" && ((string?)s["detail"])!.StartsWith("roster "));

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
