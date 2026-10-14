using System.Globalization;
using System.Text.Json;
using BusTracker.Api.Ibb;

namespace BusTracker.Api.Shapes;

public sealed record RouteShape(
    string RouteCode,
    string LineCode,
    string Direction,
    string Variant,
    double LengthMeters,
    double DurationSeconds,
    IReadOnlyList<LatLng> Points)
{
    public bool IsMainVariant => Variant == "D0";
}

public static class RouteShapeParser
{
    public static RouteShape? Parse(JsonDocument feature, double toleranceMeters)
    {
        var root = feature.RootElement;
        if (!root.TryGetProperty("properties", out var properties) || !root.TryGetProperty("geometry", out var geometry))
            return null;
        if (Text(properties, "GUZERGAH_KODU") is not { Length: > 0 } routeCode) return null;
        if (Text(geometry, "type") != "LineString") return null;

        var points = ReadPoints(geometry.GetProperty("coordinates"));
        if (points.Count < 2) return null;

        var (direction, variant) = IbbParsers.SplitRouteCode(routeCode);
        return new RouteShape(
            RouteCode: routeCode,
            LineCode: Text(properties, "HAT_KODU")?.Trim() ?? routeCode.Split('_')[0],
            Direction: direction,
            Variant: variant,
            LengthMeters: Number(properties, "UZUNLUK"),
            DurationSeconds: Number(properties, "SURE"),
            Points: PolylineSimplifier.Simplify(points, toleranceMeters));
    }

    private static List<LatLng> ReadPoints(JsonElement coordinates) =>
        coordinates.EnumerateArray()
            .Select(c => new LatLng(Lat: c[1].GetDouble(), Lng: c[0].GetDouble()))
            .ToList();

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number) return value.GetDouble();
        var text = value.GetString()?.Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }
}
