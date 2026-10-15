using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BusTracker.Api.Endpoints;
using BusTracker.Api.Ibb;
using BusTracker.Api.Stops;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BusTracker.Tests;

public class TestAppFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset FixtureTime = new(2026, 10, 14, 13, 7, 0, TimeSpan.FromHours(3));

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bustracker-tests", Guid.NewGuid().ToString("N"));

    public FakeTimeProvider Clock { get; } = new(FixtureTime);

    public ConcurrentDictionary<string, int> IbbCalls { get; } = new();

    public string FleetXml { get; set; } = Fixture.Read("fleet-sample.xml");

    public int CallsTo(string operation) => IbbCalls.GetValueOrDefault(operation);

    protected virtual string[] InviteCodes => [];

    protected virtual int RegistrationsPerIp => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("BusTracker:DatabasePath", Path.Combine(_directory, "app.db"));
        builder.UseSetting(WebHostDefaults.WebRootKey, Path.Combine(_directory, "wwwroot"));
        builder.UseSetting("BusTracker:StaticData:SyncEnabled", "false");
        builder.UseSetting("BusTracker:Access:RegistrationsPerIpPer10Minutes", RegistrationsPerIp.ToString());
        for (var i = 0; i < InviteCodes.Length; i++)
            builder.UseSetting($"BusTracker:Access:InviteCodes:{i}", InviteCodes[i]);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddHttpClient<IbbClient>().ConfigurePrimaryHttpMessageHandler(() => new FakeIbbHandler(IbbCalls, () => FleetXml));
        });
    }

    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private bool _seeded;

    public async Task SeedStopIndexAsync()
    {
        await _seedLock.WaitAsync();
        try
        {
            if (!_seeded) await Services.GetRequiredService<StopIndexSync>().SyncAsync(CancellationToken.None);
            _seeded = true;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    public async Task<HttpClient> CreateUserClientAsync(string? invite = null)
    {
        await SeedStopIndexAsync();
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/devices", new RegisterDeviceRequest("web", "test", invite, null));
        response.EnsureSuccessStatusCode();
        var registered = await response.Content.ReadFromJsonAsync<RegisterDeviceResponse>();
        return WithKey(registered!.Key);
    }

    public HttpClient WithKey(string key)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private sealed class FakeIbbHandler(ConcurrentDictionary<string, int> calls, Func<string> fleetXml) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var action = request.Headers.GetValues("SOAPAction").Single();
            calls.AddOrUpdate(action.Trim('"').Split('/')[^1], 1, (_, n) => n + 1);
            var body = await request.Content!.ReadAsStringAsync(ct);
            var is131Y = body.Contains(">131Y<");
            var isAllLines = body.Contains("<hat_kodu></hat_kodu>") || body.Contains("<hat_kodu />");
            var (status, xml) = action switch
            {
                _ when action.Contains("DurakDetay_GYY_wYonAdi") =>
                    (HttpStatusCode.OK, isAllLines ? Fixture.Read("all-route-stops-sample.xml")
                        : is131Y ? Fixture.Read("route-stops-131Y.xml") : EmptyDataSet("DurakDetay_GYY_wYonAdi")),
                _ when action.Contains("GetFiloAracKonum_json") => (HttpStatusCode.OK, fleetXml()),
                _ when action.Contains("GetHatOtoKonum_json") =>
                    is131Y ? (HttpStatusCode.OK, Fixture.Read("live-131Y.xml")) : (HttpStatusCode.InternalServerError, Fixture.Read("fault-unknown-line.xml")),
                _ => (HttpStatusCode.InternalServerError, Fixture.Read("fault-unknown-line.xml")),
            };
            return new HttpResponseMessage(status) { Content = new StringContent(xml, Encoding.UTF8, "text/xml") };
        }

        private static string EmptyDataSet(string operation) =>
            $"""<?xml version="1.0" encoding="utf-8"?><soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><{operation}Response xmlns="http://tempuri.org/"><{operation}Result><NewDataSet xmlns="" /></{operation}Result></{operation}Response></soap:Body></soap:Envelope>""";
    }
}

public sealed class InviteOnlyAppFactory : TestAppFactory
{
    public const string Invite = "family-2026";

    protected override string[] InviteCodes => [Invite];
}

public sealed class StrictRateLimitAppFactory : TestAppFactory
{
    protected override int RegistrationsPerIp => 2;
}
