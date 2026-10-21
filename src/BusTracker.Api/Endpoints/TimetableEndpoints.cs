using BusTracker.Api.Lines;
using BusTracker.Api.Stops;
using BusTracker.Api.Timetables;

namespace BusTracker.Api.Endpoints;

public static class TimetableEndpoints
{
    private const int MaxStops = 20;
    private const int MaxLines = 40;
    private static readonly string[] DayTypes = ["I", "C", "P"];

    public static IEndpointRouteBuilder MapTimetableEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/lines/{code}/timetable", GetTimetable);
        app.MapGet("/api/announcements", GetAnnouncements);
        return app;
    }

    private static async Task<IResult> GetTimetable(string code, string? day, LineDataService data, LinePlans plans, CancellationToken ct)
    {
        if (LineCode.Normalize(code) is not { } line) return LineCode.Invalid();
        if (await LineCode.LoadStopsAsync(line, data, ct) is not { } stops) return LineCode.Unknown(line);
        if (await plans.GetAsync(line, ct) is not { } plan) return Unavailable(line);

        var today = plan.Day;
        var dayType = DayTypes.FirstOrDefault(d => d == day?.Trim().ToUpperInvariant()) ?? today.DayType;
        var on = dayType == today.DayType ? today : today with { DayType = dayType, Holiday = false };
        var directions = stops
            .GroupBy(s => s.Direction)
            .OrderBy(g => g.Key)
            .Select(g => new TimetableDirectionView(
                g.Key,
                g.First().DirectionName,
                g.MinBy(s => s.Seq)!.Name,
                (plan.Table?.Day(g.Key, dayType) ?? [])
                    .Select(t => new TimetableTripView(t.Clock, today.At(t.Minute), t.RouteCode, t.Variant, plan.IsCancelled(g.Key, t, on)))
                    .ToList()))
            .ToList();
        return Results.Ok(new TimetableResponse(line, dayType, on.Holiday, today.DayType, plan.Table is not null, directions, plan.Announcements));
    }

    private static async Task<AnnouncementsResponse> GetAnnouncements(
        string? lines, string? stops, StopIndexRepository index, LinePlans plans, CancellationToken ct)
    {
        var codes = Split(lines).Select(LineCode.Normalize).OfType<string>().ToHashSet();
        foreach (var stop in Split(stops).Take(MaxStops))
            codes.UnionWith((await index.GetLinesAtStopAsync(stop, ct)).Select(l => l.LineCode));
        return new AnnouncementsResponse(codes.Order().Take(MaxLines).SelectMany(plans.AnnouncementsFor).ToList());
    }

    private static IEnumerable<string> Split(string? list) =>
        (list ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct();

    private static IResult Unavailable(string line) =>
        Results.Problem($"Timetable for line {line} is unavailable right now.", statusCode: StatusCodes.Status503ServiceUnavailable);
}

public sealed record TimetableTripView(string Time, DateTimeOffset At, string RouteCode, string Variant, bool Cancelled);

public sealed record TimetableDirectionView(string Direction, string Name, string FirstStop, IReadOnlyList<TimetableTripView> Trips);

public sealed record TimetableResponse(
    string Line, string DayType, bool Holiday, string TodayDayType, bool Available,
    IReadOnlyList<TimetableDirectionView> Directions, IReadOnlyList<AnnouncementView> Announcements);

public sealed record AnnouncementsResponse(IReadOnlyList<AnnouncementView> Announcements);
