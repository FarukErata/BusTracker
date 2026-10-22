using BusTracker.Api.Ibb;
using BusTracker.Api.Lines;
using BusTracker.Api.Tracking;

namespace BusTracker.Api.Timetables;

public sealed record WaitingBus(string DoorNo, string RouteCode);

public sealed record PlannedTripView(DateTimeOffset Departs, DateTimeOffset? AtStop, string RouteCode, string Variant, bool Cancelled, bool NextDay);

public sealed record PlannedView(
    string Direction, string FirstStop, string DayType, bool Holiday, IReadOnlyList<PlannedTripView> Next, DateOnly NextDate);

public sealed record AnnouncementView(
    string Line, string Kind, string Message, string PostedAt, string? Direction, string? FromStop, string? Departure, string? Reason)
{
    public static AnnouncementView From(Announcement a, string? direction) =>
        new(a.Line, a.CancelledTrip ? "cancelled" : "notice", a.Message, a.PostedAt, direction, a.FromStop,
            a.Departure?.ToString("HH:mm"), a.Reason);
}

public sealed class LinePlan(Timetable? table, LineTracks tracks, ServiceDay day, IReadOnlyList<(Announcement Item, string? Direction)> notices)
{
    private const int NextShown = 3;
    private const int SpareTrips = 10;
    private static readonly TimeSpan LateDeparture = TimeSpan.FromMinutes(2);

    public Timetable? Table => table;

    public ServiceDay Day => day;

    public IReadOnlyList<AnnouncementView> Announcements =>
        notices.Select(n => AnnouncementView.From(n.Item, n.Direction)).ToList();

    public bool IsCancelled(string direction, PlannedDeparture trip, ServiceDay on) =>
        on == day && notices.Any(n => n.Direction == direction && n.Item.Departure is { } d && ServiceDay.MinuteOf(d) == trip.Minute);

    public (PlannedView? View, IReadOnlyDictionary<string, PlannedTripView> Waiting) For(
        string direction, string? stopCode, IReadOnlyList<WaitingBus> waiting, DateTimeOffset now)
    {
        if (table is null || tracks.Main(direction) is not { } main)
            return (null, new Dictionary<string, PlannedTripView>());

        var upcoming = table.From(direction, day, now - LateDeparture)
            .Where(d => Serves(d.Trip.RouteCode, direction, stopCode))
            .Take(waiting.Count + NextShown + SpareTrips)
            .Select(d => ToView(d, direction, stopCode))
            .ToList();

        var taken = new HashSet<int>();
        var assigned = new Dictionary<string, PlannedTripView>();
        foreach (var bus in waiting)
        {
            var index = Pick(upcoming, taken, bus.RouteCode);
            if (index < 0) continue;
            taken.Add(index);
            assigned[bus.DoorNo] = upcoming[index];
        }

        var next = upcoming
            .Where((trip, i) => !taken.Contains(i) && (trip.AtStop ?? trip.Departs) >= now - LateDeparture)
            .Take(NextShown)
            .ToList();
        return (new PlannedView(direction, main.Stops.FirstOrDefault()?.Name ?? "", day.DayType, day.Holiday, next, day.Next.Date), assigned);
    }

    private static int Pick(IReadOnlyList<PlannedTripView> trips, HashSet<int> taken, string routeCode)
    {
        var free = Enumerable.Range(0, trips.Count).Where(i => !taken.Contains(i) && !trips[i].Cancelled).ToList();
        var sameRoute = free.Where(i => trips[i].RouteCode == routeCode).ToList();
        if (sameRoute.Count > 0) return sameRoute[0];
        return free.Count > 0 ? free[0] : -1;
    }

    private bool Serves(string routeCode, string direction, string? stopCode) =>
        stopCode is null || tracks.For(routeCode, direction)?.FindStop(stopCode) is not null;

    private PlannedTripView ToView(DatedDeparture d, string direction, string? stopCode) =>
        new(d.At, AtStop(d, direction, stopCode), d.Trip.RouteCode, d.Trip.Variant, IsCancelled(direction, d.Trip, d.Day), d.Day != day);

    private DateTimeOffset? AtStop(DatedDeparture d, string direction, string? stopCode)
    {
        if (stopCode is null || tracks.For(d.Trip.RouteCode, direction) is not { } track || track.FindStop(stopCode) is not { } stop)
            return null;
        return d.At.AddSeconds(Math.Round(stop.AlongMeters / Math.Max(track.ScheduledSpeedMps, 1)));
    }
}

public sealed class LinePlans(TimetableService timetables, AnnouncementService announcements, LineDataService data, TimeProvider clock)
{
    public async Task<LinePlan?> GetAsync(string lineCode, CancellationToken ct, bool waitForTimetable = true)
    {
        try
        {
            var tracks = await data.GetTracksAsync(lineCode, ct);
            var table = waitForTimetable ? await timetables.GetAsync(lineCode, ct) : timetables.Peek(lineCode);
            var day = ServiceDay.Of(clock.GetUtcNow());
            var firstStops = tracks.Mains.ToDictionary(t => t.Direction, t => t.Stops.FirstOrDefault()?.Name ?? "");
            var notices = announcements.For(lineCode)
                .Select(a => (a, Announcements.DirectionOf(a, table, day, firstStops)))
                .ToList();
            return new LinePlan(table, tracks, day, notices);
        }
        catch (Exception e) when (e is IbbException or HttpRequestException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyDictionary<string, LinePlan>> GetManyAsync(IEnumerable<string> lineCodes, CancellationToken ct)
    {
        var codes = lineCodes.Distinct().ToList();
        var plans = await Task.WhenAll(codes.Select(c => GetAsync(c, ct, waitForTimetable: false)));
        return codes.Zip(plans).Where(p => p.Second is not null).ToDictionary(p => p.First, p => p.Second!);
    }

    private const int MaxAnnouncedLines = 40;

    public IReadOnlyList<AnnouncementView> AnnouncementsFor(string lineCode) =>
        announcements.For(lineCode).Select(a => AnnouncementView.From(a, null)).ToList();

    public IReadOnlyList<AnnouncementView> AnnouncementsFor(IEnumerable<string> lineCodes) =>
        lineCodes.Distinct().Order().Take(MaxAnnouncedLines).SelectMany(code => AnnouncementsFor(code)).ToList();

    public static IReadOnlyList<WaitingBus> WaitingAmong(IEnumerable<Arrival> arrivals) =>
        arrivals.Where(a => a.NotDeparted).Select(a => new WaitingBus(a.Bus.Bus.DoorNo, a.Bus.Bus.RouteCode)).ToList();
}
