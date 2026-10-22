using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace BusTracker.Api.Rail;

public sealed record RailLine(int Id, string Name, string Color, string Description);

public sealed record RailStation(int Id, string Name, int LineId, string Line, int Order, double? Lat, double? Lng);

public sealed record RailDirection(int Id, int LineId, string From, string To);

public sealed record RailNotice(int LineId, string Text, DateTimeOffset? UpdatedAt);

public sealed class MetroException(string message) : Exception(message);

public sealed class MetroClient(HttpClient http)
{
    public async Task<IReadOnlyList<RailLine>> GetLinesAsync(CancellationToken ct) =>
        (await GetAsync<List<LineRow>>("GetLines", ct))
            .Where(l => l.IsActive != false)
            .Select(l => new RailLine(l.Id, l.Name.Trim(), Hex(l.Color), (l.LongDescription ?? "").Trim()))
            .ToList();

    public async Task<IReadOnlyList<RailStation>> GetStationsAsync(CancellationToken ct) =>
        (await GetAsync<List<StationRow>>("GetStations", ct))
            .Where(s => s.IsActive != false)
            .Select(s => new RailStation(
                s.Id, string.IsNullOrWhiteSpace(s.Description) ? s.Name.Trim() : s.Description.Trim(), s.LineId, s.LineName.Trim(), s.Order,
                Coordinate(s.DetailInfo?.Latitude), Coordinate(s.DetailInfo?.Longitude)))
            .ToList();

    public async Task<IReadOnlyList<RailDirection>> GetDirectionsAsync(int lineId, CancellationToken ct) =>
        (await GetAsync<List<DirectionRow>>($"GetDirectionById/{lineId}", ct))
            .Select(d =>
            {
                var ends = d.DirectionName.Split("->", 2, StringSplitOptions.TrimEntries);
                return new RailDirection(d.DirectionId, d.LineId, ends[0], ends.Length > 1 ? ends[1] : "");
            })
            .ToList();

    public async Task<IReadOnlyList<TimeOnly>> GetTimeTableAsync(int stationId, int directionId, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("GetTimeTable", new { BoardingStationId = stationId, DirectionId = directionId }, ct);
        var rows = Read<List<TimeTableRow>>(await response.Content.ReadAsStringAsync(ct));
        return rows.SelectMany(r => r.TimeInfos?.Times ?? [])
            .Select(t => TimeOnly.TryParseExact(t.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : (TimeOnly?)null)
            .OfType<TimeOnly>()
            .ToList();
    }

    public async Task<IReadOnlyList<RailNotice>> GetServiceStatusesAsync(CancellationToken ct) =>
        (await GetAsync<List<StatusRow>>("GetServiceStatuses", ct))
            .Where(s => s.IsActive != false && !string.IsNullOrWhiteSpace(s.Description))
            .Select(s => new RailNotice(s.LineId, s.Description!.Trim(), UpdatedAt(s.UpdateDate)))
            .ToList();

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, ct);
        return Read<T>(await response.Content.ReadAsStringAsync(ct));
    }

    private static T Read<T>(string body)
    {
        var end = body.LastIndexOf('}');
        if (end < 0) throw new MetroException("Metro İstanbul answered without data");
        var envelope = JsonSerializer.Deserialize<Envelope<T>>(body[..(end + 1)])
            ?? throw new MetroException("Metro İstanbul answered without data");
        if (!envelope.Success || envelope.Data is null) throw new MetroException("Metro İstanbul refused the request");
        return envelope.Data;
    }

    private static string Hex(ColorRow? color)
    {
        static int Part(string? text) => int.TryParse(text, out var value) ? Math.Clamp(value, 0, 255) : 128;
        return color is null ? "#808080" : $"#{Part(color.Color_R):X2}{Part(color.Color_G):X2}{Part(color.Color_B):X2}";
    }

    private static double? Coordinate(string? text) =>
        double.TryParse(text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value != 0 ? value : null;

    private static DateTimeOffset? UpdatedAt(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? new DateTimeOffset(at, Ibb.IbbParsers.IstanbulOffset)
            : null;

    private sealed record Envelope<T>(bool Success, T? Data);

    private sealed record ColorRow(string? Color_R, string? Color_G, string? Color_B);

    private sealed record LineRow(int Id, string Name, ColorRow? Color, string? LongDescription, bool? IsActive);

    private sealed record DetailRow(string? Latitude, string? Longitude);

    private sealed record StationRow(int Id, string Name, string? Description, int LineId, string LineName, int Order, bool? IsActive, DetailRow? DetailInfo);

    private sealed record DirectionRow(int LineId, int DirectionId, string DirectionName);

    private sealed record TimeInfoRow(List<string>? Times);

    private sealed record TimeTableRow(TimeInfoRow? TimeInfos);

    private sealed record StatusRow(int LineId, string? Description, bool? IsActive, string? UpdateDate);
}
