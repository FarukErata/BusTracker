using System.Text;
using System.Xml.Linq;

namespace BusTracker.Api.Ibb;

public sealed class IbbClient(HttpClient http)
{
    private const string FleetService = "FiloDurum/SeferGerceklesme.asmx";
    private const string RouteService = "ibb/ibb.asmx";

    public async Task<IReadOnlyList<LiveBus>> GetLiveBusesAsync(string lineCode, CancellationToken ct)
    {
        var result = await CallAsync(FleetService, "GetHatOtoKonum_json", new() { ["HatKodu"] = lineCode }, ct);
        return IbbParsers.ParseLiveBuses(result);
    }

    public async Task<IReadOnlyList<RouteStop>> GetRouteStopsAsync(string lineCode, CancellationToken ct)
    {
        var result = await CallAsync(RouteService, "DurakDetay_GYY_wYonAdi", new() { ["hat_kodu"] = lineCode }, ct);
        return IbbParsers.ParseRouteStops(result);
    }

    private async Task<XElement> CallAsync(
        string service, string operation, Dictionary<string, string> parameters, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, service)
        {
            Content = new StringContent(SoapEnvelope.Build(operation, parameters), Encoding.UTF8, "text/xml"),
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", SoapEnvelope.SoapAction(operation));

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        return SoapEnvelope.ReadResult(operation, text);
    }
}
