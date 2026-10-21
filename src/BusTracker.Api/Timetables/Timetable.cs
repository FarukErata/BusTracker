using BusTracker.Api.Ibb;

namespace BusTracker.Api.Timetables;

public sealed record PlannedDeparture(string RouteCode, string Variant, int Minute)
{
    public string Clock => $"{Minute / 60 % 24:00}:{Minute % 60:00}";
}

public sealed record DatedDeparture(PlannedDeparture Trip, ServiceDay Day)
{
    public DateTimeOffset At => Day.At(Trip.Minute);
}

public sealed class Timetable
{
    private readonly Dictionary<(string Direction, string DayType), IReadOnlyList<PlannedDeparture>> _byDay;

    public Timetable(IReadOnlyList<PlannedTrip> trips)
    {
        _byDay = trips
            .GroupBy(t => (t.Direction, t.DayType))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PlannedDeparture>)g
                    .Select(t => new PlannedDeparture(t.RouteCode, IbbParsers.SplitRouteCode(t.RouteCode).Variant, ServiceDay.MinuteOf(t.Departure)))
                    .Distinct()
                    .OrderBy(d => d.Minute)
                    .ThenBy(d => d.RouteCode)
                    .ToList());
        Directions = _byDay.Keys.Select(k => k.Direction).Distinct().Order().ToList();
    }

    public IReadOnlyList<string> Directions { get; }

    public IReadOnlyList<PlannedDeparture> Day(string direction, string dayType) =>
        _byDay.GetValueOrDefault((direction, dayType)) ?? [];

    public IEnumerable<DatedDeparture> From(string direction, ServiceDay day, DateTimeOffset since)
    {
        var minute = day.MinuteOf(since);
        foreach (var trip in Day(direction, day.DayType).Where(d => d.Minute >= minute))
            yield return new DatedDeparture(trip, day);
        var next = day.Next;
        foreach (var trip in Day(direction, next.DayType))
            yield return new DatedDeparture(trip, next);
    }
}
