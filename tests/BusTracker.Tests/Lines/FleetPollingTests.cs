using System.Net.Http.Json;
using BusTracker.Api.Configuration;
using BusTracker.Api.Data;
using BusTracker.Api.Endpoints;
using BusTracker.Api.Lines;
using BusTracker.Api.Shapes;
using BusTracker.Api.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BusTracker.Tests.Lines;

public sealed class FleetPollingTests
{
    private const string Roster = "GetHatOtoKonum_json";
    private const string Fleet = "GetFiloAracKonum_json";

    [Fact]
    public async Task Live_TakesPositionsFromTheFleetWhenTheyAreNewer()
    {
        using var factory = new TestAppFactory();
        var client = await factory.CreateUserClientAsync();

        var live = await client.GetFromJsonAsync<LineLiveResponse>("/api/lines/131Y/live");

        var moved = live!.Buses.Single(b => b.DoorNo == "O2031");
        Assert.Equal(40.894012, moved.Lat, 6);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 13, 6, 59, TimeSpan.FromHours(3)), moved.ReportedAt);

        var olderInFleet = live.Buses.Single(b => b.DoorNo == "C-114");
        Assert.Equal(41.03596, olderInFleet.Lat, 5);
        Assert.DoesNotContain(live.Buses, b => b.DoorNo == "A-001");
    }

    [Fact]
    public async Task Fleet_IsFetchedOncePerIntervalAndSharedByLines()
    {
        using var factory = new TestAppFactory();
        var client = await factory.CreateUserClientAsync();

        await client.GetAsync("/api/lines/131Y/live");
        await client.GetAsync("/api/lines/131Y/live");
        await client.GetAsync("/api/stops/402011/arrivals");
        Assert.Equal(1, factory.CallsTo(Fleet));

        factory.Clock.Advance(TimeSpan.FromSeconds(60));
        await client.GetAsync("/api/lines/131Y/live");
        await factory.Services.GetRequiredService<LineDataService>().WhenIdleAsync();

        Assert.Equal(2, factory.CallsTo(Fleet));
    }

    [Fact]
    public async Task Roster_IsRefreshedOnlyAfterItsMaxAge()
    {
        using var factory = new TestAppFactory();
        var client = await factory.CreateUserClientAsync();

        await client.GetAsync("/api/lines/131Y/live");
        factory.Clock.Advance(TimeSpan.FromMinutes(14));
        await client.GetAsync("/api/lines/131Y/live");
        Assert.Equal(1, factory.CallsTo(Roster));

        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await client.GetAsync("/api/lines/131Y/live");
        await factory.Services.GetRequiredService<LineDataService>().WhenIdleAsync();

        Assert.Equal(2, factory.CallsTo(Roster));
    }

    [Fact]
    public async Task KnownLine_AnswersWithoutWaitingForItsRosterCall()
    {
        using var factory = new TestAppFactory();
        var client = await factory.CreateUserClientAsync();
        await client.GetAsync("/api/lines/131Y/live");

        factory.RosterDelay = TimeSpan.FromSeconds(3);
        factory.Clock.Advance(TimeSpan.FromMinutes(15));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var live = await client.GetFromJsonAsync<LineLiveResponse>("/api/lines/131Y/live");
        timer.Stop();

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1.5), $"took {timer.Elapsed}");
        Assert.NotEmpty(live!.Buses);
        Assert.Equal(2, factory.CallsTo(Roster));
        await factory.Services.GetRequiredService<LineDataService>().WhenIdleAsync();
    }

    [Fact]
    public async Task BusLeavingTheEndOfItsRoute_FlipsDirectionWithoutARosterCall()
    {
        using var factory = new TestAppFactory();
        var client = await factory.CreateUserClientAsync();
        var tracks = await TracksAsync(factory);
        var first = await client.GetFromJsonAsync<LineLiveResponse>("/api/lines/131Y/live");
        var bus = first!.Buses.First(b => b.RouteCode == "131Y_D_D0" && b.Status == "ok");
        var outbound = tracks.Main("D")!;
        var inbound = tracks.Main("G")!;

        await MoveAsync(factory, client, bus.DoorNo, outbound.Geometry.PointAt(outbound.Stops[^1].AlongMeters - 20), "13:07:50");
        var turned = await MoveAsync(factory, client, bus.DoorNo, inbound.Geometry.PointAt(400), "13:08:50");

        var flipped = turned.Buses.Single(b => b.DoorNo == bus.DoorNo);
        Assert.Equal("131Y_G_D0", flipped.RouteCode);
        Assert.Equal("G", flipped.Direction);
        Assert.Equal(inbound.Stops[^1].Name, flipped.Destination);
        Assert.Equal(1, factory.CallsTo(Roster));
    }

    [Fact]
    public async Task BusJumpingBackMidRoute_IsHiddenUntilTheRosterIsRechecked()
    {
        using var factory = new TestAppFactory();
        var client = await factory.CreateUserClientAsync();
        var tracks = await TracksAsync(factory);
        var first = await client.GetFromJsonAsync<LineLiveResponse>("/api/lines/131Y/live");
        var bus = first!.Buses.Where(b => b.RouteCode == "131Y_D_D0" && b.Status == "ok").MaxBy(b => b.AlongMeters)!;
        var route = tracks.Main("D")!;
        var back = bus.AlongMeters!.Value * 0.5;

        var jumped = await MoveAsync(factory, client, bus.DoorNo, route.Geometry.PointAt(back), "13:07:50");
        Assert.DoesNotContain(jumped.Buses, b => b.DoorNo == bus.DoorNo);

        var stillHidden = await MoveAsync(factory, client, bus.DoorNo, route.Geometry.PointAt(back + 100), "13:08:50");
        Assert.DoesNotContain(stillHidden.Buses, b => b.DoorNo == bus.DoorNo);
        Assert.Equal(1, factory.CallsTo(Roster));

        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var rechecked = await RefreshedAsync(factory, client);
        Assert.Equal(2, factory.CallsTo(Roster));
        Assert.Contains(rechecked.Buses, b => b.DoorNo == bus.DoorNo);
    }

    [Fact]
    public async Task OffRouteBus_DoesNotSpendRosterCallsOnRechecks()
    {
        using var factory = new TestAppFactory();
        var client = await factory.CreateUserClientAsync();
        var first = await client.GetFromJsonAsync<LineLiveResponse>("/api/lines/131Y/live");
        var bus = first!.Buses.First(b => b.Status == "ok");

        var offRoute = await MoveAsync(factory, client, bus.DoorNo, new LatLng(41.25, 28.75), "13:07:50");
        Assert.Equal("off-route", offRoute.Buses.Single(b => b.DoorNo == bus.DoorNo).Status);

        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        await client.GetAsync("/api/lines/131Y/live");

        Assert.Equal(1, factory.CallsTo(Roster));
    }

    [Fact]
    public async Task Memory_SurvivesARestartAndServesALineWithoutItsRoster()
    {
        using var factory = new TestAppFactory();
        var client = await factory.CreateUserClientAsync();
        await client.GetAsync("/api/lines/131Y/live");

        var restarted = new BusLineMemory(
            factory.Services.GetRequiredService<Database>(),
            factory.Services.GetRequiredService<IOptions<BusTrackerOptions>>(),
            factory.Clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BusLineMemory>.Instance);
        var known = await restarted.OnLineAsync("131Y", CancellationToken.None);

        Assert.Equal(11, known.Count);
        Assert.Contains(known, a => a.DoorNo == "O2031" && a.RouteCode == "131Y_D_D0");
        Assert.Equal(factory.Clock.GetUtcNow(), restarted.RosterAt("131Y"));

        factory.Clock.Advance(TimeSpan.FromHours(72));
        Assert.Empty(await restarted.OnLineAsync("131Y", CancellationToken.None));
    }

    private static Task<LineTracks> TracksAsync(TestAppFactory factory) =>
        factory.Services.GetRequiredService<LineDataService>().GetTracksAsync("131Y", CancellationToken.None);

    private static async Task<LineLiveResponse> MoveAsync(TestAppFactory factory, HttpClient client, string doorNo, LatLng at, string time)
    {
        factory.FleetXml = FleetWith(doorNo, at.Lat, at.Lng, time);
        factory.Clock.Advance(TimeSpan.FromSeconds(60));
        return await RefreshedAsync(factory, client);
    }

    private static async Task<LineLiveResponse> RefreshedAsync(TestAppFactory factory, HttpClient client)
    {
        await client.GetAsync("/api/lines/131Y/live");
        await factory.Services.GetRequiredService<LineDataService>().WhenIdleAsync();
        return (await client.GetFromJsonAsync<LineLiveResponse>("/api/lines/131Y/live"))!;
    }

    private static string FleetWith(string doorNo, double lat, double lng, string time) =>
        $$"""<?xml version="1.0" encoding="utf-8"?><soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><GetFiloAracKonum_jsonResponse xmlns="http://tempuri.org/"><GetFiloAracKonum_jsonResult>[{"KapiNo":"{{doorNo}}","Saat":"{{time}}","Boylam":"{{lng.ToString(System.Globalization.CultureInfo.InvariantCulture)}}","Enlem":"{{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}}"}]</GetFiloAracKonum_jsonResult></GetFiloAracKonum_jsonResponse></soap:Body></soap:Envelope>""";

    [Fact]
    public async Task Health_ReportsCallsInTheLastHour()
    {
        using var factory = new TestAppFactory();
        var client = await factory.CreateUserClientAsync();

        await client.GetAsync("/api/lines/131Y/live");
        var health = await client.GetFromJsonAsync<HealthResponse>("/health");

        Assert.Equal(new IbbCallsHealth(2, 1, 1), health!.IbbCallsLastHour);
        Assert.Equal(3, health.FleetVehicles);
    }
}
