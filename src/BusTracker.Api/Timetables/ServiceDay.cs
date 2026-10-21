using BusTracker.Api.Ibb;

namespace BusTracker.Api.Timetables;

public sealed record ServiceDay(DateOnly Date, string DayType, bool Holiday)
{
    public const int StartMinute = 3 * 60;

    private static readonly (int Month, int Day)[] FixedHolidays =
        [(1, 1), (4, 23), (5, 1), (5, 19), (7, 15), (8, 30), (10, 29)];

    private static readonly HashSet<DateOnly> MovingHolidays =
    [
        new(2026, 3, 20), new(2026, 3, 21), new(2026, 3, 22),
        new(2026, 5, 27), new(2026, 5, 28), new(2026, 5, 29), new(2026, 5, 30),
    ];

    public DateTimeOffset Midnight => new(Date.ToDateTime(TimeOnly.MinValue), IbbParsers.IstanbulOffset);

    public ServiceDay Next => On(Date.AddDays(1));

    public DateTimeOffset At(int minute) => Midnight.AddMinutes(minute);

    public DateTimeOffset Ends => At(24 * 60 + StartMinute);

    public int MinuteOf(DateTimeOffset at) => (int)Math.Floor((at - Midnight).TotalMinutes);

    public static ServiceDay Of(DateTimeOffset now)
    {
        var local = now.ToOffset(IbbParsers.IstanbulOffset).AddMinutes(-StartMinute);
        return On(DateOnly.FromDateTime(local.DateTime));
    }

    public static ServiceDay On(DateOnly date)
    {
        var holiday = IsHoliday(date);
        var type = holiday ? "P" : date.DayOfWeek switch
        {
            DayOfWeek.Saturday => "C",
            DayOfWeek.Sunday => "P",
            _ => "I",
        };
        return new ServiceDay(date, type, holiday);
    }

    public static int MinuteOf(TimeOnly clock)
    {
        var minute = clock.Hour * 60 + clock.Minute;
        return minute < StartMinute ? minute + 24 * 60 : minute;
    }

    private static bool IsHoliday(DateOnly date) =>
        MovingHolidays.Contains(date) || FixedHolidays.Contains((date.Month, date.Day));
}
