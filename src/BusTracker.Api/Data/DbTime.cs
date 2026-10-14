using System.Globalization;

namespace BusTracker.Api.Data;

public static class DbTime
{
    public static string? Format(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture);

    public static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset? Parse(string? value) =>
        value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
