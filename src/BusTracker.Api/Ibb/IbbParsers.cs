using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace BusTracker.Api.Ibb;

public static class IbbParsers
{
    public static readonly TimeSpan IstanbulOffset = TimeSpan.FromHours(3);

    public static IReadOnlyList<LiveBus> ParseLiveBuses(XElement result)
    {
        var json = result.Value.Trim();
        if (json.Length == 0) return [];

        var rows = JsonSerializer.Deserialize<List<LiveBusRow>>(json)
            ?? throw new IbbException("Live bus list is null");
        return rows.Select(ToLiveBus).ToList();
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

    private static RouteStop ToRouteStop(XElement table) =>
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
