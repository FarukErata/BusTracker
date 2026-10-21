using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using BusTracker.Api.Configuration;
using BusTracker.Api.Tracing;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Ibb;

public sealed class IbbClient(HttpClient http, IOptions<BusTrackerOptions> options)
{
    private const string FleetService = "FiloDurum/SeferGerceklesme.asmx";
    private const string RouteService = "ibb/ibb.asmx";
    private const string TimetableService = "UlasimAnaVeri/PlanlananSeferSaati.asmx";
    private const string AnnouncementService = "UlasimDinamikVeri/Duyurular.asmx";
    private const string RouteStopsOperation = "DurakDetay_GYY_wYonAdi";

    private readonly TimeSpan _callTimeout = TimeSpan.FromSeconds(options.Value.Ibb.TimeoutSeconds);
    private readonly TimeSpan _bulkTimeout = TimeSpan.FromMinutes(options.Value.StaticData.DownloadTimeoutMinutes);

    public async Task<IReadOnlyList<LiveBus>> GetLiveBusesAsync(string lineCode, CancellationToken ct)
    {
        var result = await CallAsync(FleetService, "GetHatOtoKonum_json", new() { ["HatKodu"] = lineCode }, ct);
        return IbbParsers.ParseLiveBuses(result);
    }

    public async Task<IReadOnlyList<FleetVehicle>> GetFleetAsync(DateTimeOffset now, CancellationToken ct)
    {
        var result = await CallAsync(FleetService, "GetFiloAracKonum_json", new(), ct);
        return IbbParsers.ParseFleet(result, now);
    }

    public async Task<IReadOnlyList<RouteStop>> GetRouteStopsAsync(string lineCode, CancellationToken ct)
    {
        var result = await CallAsync(RouteService, RouteStopsOperation, new() { ["hat_kodu"] = lineCode }, ct);
        return IbbParsers.ParseRouteStops(result);
    }

    public async Task<IReadOnlyList<PlannedTrip>> GetPlannedTripsAsync(string lineCode, CancellationToken ct)
    {
        var result = await CallAsync(TimetableService, "GetPlanlananSeferSaati_json", new() { ["HatKodu"] = lineCode }, ct);
        return IbbParsers.ParsePlannedTrips(result);
    }

    public async Task<IReadOnlyList<IbbAnnouncement>> GetAnnouncementsAsync(CancellationToken ct)
    {
        var result = await CallAsync(AnnouncementService, "GetDuyurular_json", new(), ct);
        return IbbParsers.ParseAnnouncements(result);
    }

    public async IAsyncEnumerable<RouteStop> StreamAllRouteStopsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_bulkTimeout);
        using var response = await SendAsync(RouteService, RouteStopsOperation, new() { ["hat_kodu"] = "" },
            HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
            SoapEnvelope.ReadResult(RouteStopsOperation, await response.Content.ReadAsStringAsync(timeout.Token));

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true, IgnoreWhitespace = true });
        while (!reader.EOF)
        {
            if (reader is { NodeType: XmlNodeType.Element, LocalName: "Table" })
                yield return IbbParsers.ToRouteStop((XElement)await XNode.ReadFromAsync(reader, timeout.Token));
            else
                await reader.ReadAsync();
        }
    }

    private async Task<XElement> CallAsync(
        string service, string operation, Dictionary<string, string> parameters, CancellationToken ct)
    {
        using var span = Telemetry.Span("ibb", string.Join(' ', parameters.Values.Where(v => v.Length > 0).Prepend(operation)));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_callTimeout);
        try
        {
            using var response = await SendAsync(service, operation, parameters, HttpCompletionOption.ResponseContentRead, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            return SoapEnvelope.ReadResult(operation, text);
        }
        catch (Exception e)
        {
            span.Fail(e.GetType().Name);
            throw;
        }
    }

    private Task<HttpResponseMessage> SendAsync(
        string service, string operation, Dictionary<string, string> parameters, HttpCompletionOption completion, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, service)
        {
            Content = new StringContent(SoapEnvelope.Build(operation, parameters), Encoding.UTF8, "text/xml"),
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", SoapEnvelope.SoapAction(operation));
        return http.SendAsync(request, completion, ct);
    }
}
