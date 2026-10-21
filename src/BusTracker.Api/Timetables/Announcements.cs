using System.Globalization;
using System.Text.RegularExpressions;
using BusTracker.Api.Endpoints;
using BusTracker.Api.Ibb;
using BusTracker.Api.Stops;

namespace BusTracker.Api.Timetables;

public sealed record Announcement(
    string Line, bool CancelledTrip, string Message, string PostedAt, string? FromStop, TimeOnly? Departure, string? Reason);

public static partial class Announcements
{
    private static readonly TimeSpan KeptAfterDeparture = TimeSpan.FromMinutes(15);
    private static readonly HashSet<string> StopNoise = ["SON", "DURAK", "SONDURAK", "PERON", "PERONLAR", "PERONU"];

    public static Announcement From(IbbAnnouncement row)
    {
        var line = LineCode.Normalize(row.LineCode) ?? row.LineCode.Trim().ToUpperInvariant();
        var trip = row.Kind.StartsWith("Sefer", StringComparison.OrdinalIgnoreCase) ? CancelledTrip().Match(row.Message) : null;
        if (trip is not { Success: true })
            return new Announcement(line, false, row.Message, row.PostedAt, null, null, null);

        var departure = TimeOnly.ParseExact(trip.Groups[2].Value, "HH:mm", CultureInfo.InvariantCulture);
        return new Announcement(line, true, row.Message, row.PostedAt, trip.Groups[1].Value.Trim(), departure, trip.Groups[3].Value.Trim());
    }

    public static bool StillRelevant(Announcement a, ServiceDay day, DateTimeOffset now) =>
        a.Departure is not { } departure || day.At(ServiceDay.MinuteOf(departure)) >= now - KeptAfterDeparture;

    public static string? DirectionOf(Announcement a, Timetable? table, ServiceDay day, IReadOnlyDictionary<string, string> firstStops)
    {
        if (a.Departure is not { } departure) return null;

        var minute = ServiceDay.MinuteOf(departure);
        var byTime = table?.Directions.Where(d => table.Day(d, day.DayType).Any(t => t.Minute == minute)).ToList() ?? [];
        if (byTime.Count == 1) return byTime[0];

        var candidates = byTime.Count > 0 ? byTime : firstStops.Keys.ToList();
        var stop = StopKey(a.FromStop ?? "");
        var byName = candidates.Where(d => firstStops.TryGetValue(d, out var name) && SameStop(StopKey(name), stop)).ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    private static bool SameStop(string a, string b) =>
        a.Length > 0 && b.Length > 0 && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal));

    private static string StopKey(string name) =>
        string.Join(' ', SearchText.Normalize(name).Split(' ').Where(w => !StopNoise.Contains(w)));

    [GeneratedRegex(@"^(.+?) ?dan Saat (\d\d:\d\d) de hareket etmesi planlanan seferimiz +(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CancelledTrip();
}
