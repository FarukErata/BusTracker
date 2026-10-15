using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace BusTracker.Api.Ibb;

public static class IbbParsers
{
    public static readonly TimeSpan IstanbulOffset = TimeSpan.FromHours(3);
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public static IReadOnlyList<LiveBus> ParseLiveBuses(XElement result)
    {
        var json = result.Value.Trim();
        if (json.Length == 0) return [];

        var rows = JsonSerializer.Deserialize<List<LiveBusRow>>(json)
            ?? throw new IbbException("Live bus list is null");
        return rows.Select(ToLiveBus).ToList();
    }

    public static IReadOnlyList<FleetVehicle> ParseFleet(XElement result, DateTimeOffset now)
    {
        var json = result.Value.Trim();
        if (json.Length == 0) return [];

        var rows = JsonSerializer.Deserialize<List<FleetRow>>(json)
            ?? throw new IbbException("Fleet list is null");
        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.DoorNo) && TryParseDouble(r.Lat, out _) && TryParseDouble(r.Lng, out _)
                && TimeOnly.TryParseExact(r.Time, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .Select(r => new FleetVehicle(
                r.DoorNo!.Trim(), ParseDouble(r.Lat!), ParseDouble(r.Lng!), InferIstanbulTime(r.Time!, now)))
            .ToList();
    }

    public static DateTimeOffset InferIstanbulTime(string clockText, DateTimeOffset now)
    {
        var local = now.ToOffset(IstanbulOffset);
        var time = TimeOnly.ParseExact(clockText, "HH:mm:ss", CultureInfo.InvariantCulture);
        var at = new DateTimeOffset(DateOnly.FromDateTime(local.DateTime).ToDateTime(time), IstanbulOffset);
        return at > local + FutureTolerance ? at.AddDays(-1) : at;
    }

    public static IReadOnlyList<RouteStop> ParseRouteStops(XElement result) =>
        result.Descendants("Table")
            .Select(ToRouteStop)
            .OrderBy(s => s.Direction)
            .ThenBy(s => s.Seq)
            .ToList();

    public static DateTimeOffset ParseIstanbulTime(string text) =>
        new(DateTime.ParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), IstanbulOffset);

    public static (string Direction, string Variant) SplitRouteCode(string routeCode)
    {
        var parts = routeCode.Split('_');
        return parts.Length >= 3 ? (parts[1], parts[2]) : ("?", "?");
    }

    private static LiveBus ToLiveBus(LiveBusRow row)
    {
        var (direction, variant) = SplitRouteCode(row.RouteCode);
        return new LiveBus(
            DoorNo: row.DoorNo,
            Lat: ParseDouble(row.Lat),
            Lng: ParseDouble(row.Lng),
            LineCode: row.LineCode.Trim(),
            RouteCode: row.RouteCode,
            Direction: direction,
            Variant: variant,
            Destination: row.Destination.Trim(),
            ReportedAt: ParseIstanbulTime(row.ReportedAt),
            NearestStopCode: row.NearestStopCode);
    }

    public static RouteStop ToRouteStop(XElement table) =>
        new(
            LineCode: Field(table, "HATKODU"),
            Direction: Field(table, "YON"),
            DirectionName: Field(table, "YON_ADI"),
            Seq: int.Parse(Field(table, "SIRANO"), CultureInfo.InvariantCulture),
            StopCode: Field(table, "DURAKKODU"),
            Name: Field(table, "DURAKADI"),
            Lat: ParseDouble(Field(table, "YKOORDINATI")),
            Lng: ParseDouble(Field(table, "XKOORDINATI")),
            District: Field(table, "ILCEADI"));

    private static string Field(XElement table, string name) => table.Element(name)?.Value.Trim() ?? "";

    private static double ParseDouble(string text) =>
        double.Parse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static bool TryParseDouble(string? text, out double value) =>
        double.TryParse(text?.Replace(',', '.').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private sealed record FleetRow(
        [property: JsonPropertyName("KapiNo")] string? DoorNo,
        [property: JsonPropertyName("Saat")] string? Time,
        [property: JsonPropertyName("Boylam")] string? Lng,
        [property: JsonPropertyName("Enlem")] string? Lat);

    private sealed record LiveBusRow(
        [property: JsonPropertyName("kapino")] string DoorNo,
        [property: JsonPropertyName("boylam")] string Lng,
        [property: JsonPropertyName("enlem")] string Lat,
        [property: JsonPropertyName("hatkodu")] string LineCode,
        [property: JsonPropertyName("guzergahkodu")] string RouteCode,
        [property: JsonPropertyName("hatad")] string LineName,
        [property: JsonPropertyName("yon")] string Destination,
        [property: JsonPropertyName("son_konum_zamani")] string ReportedAt,
        [property: JsonPropertyName("yakinDurakKodu")] string NearestStopCode);
}
