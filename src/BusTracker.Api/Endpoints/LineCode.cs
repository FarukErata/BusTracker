using System.Text.RegularExpressions;
using BusTracker.Api.Ibb;
using BusTracker.Api.Lines;

namespace BusTracker.Api.Endpoints;

public static partial class LineCode
{
    public static string? Normalize(string code)
    {
        var trimmed = code.Trim().ToUpperInvariant();
        return Pattern().IsMatch(trimmed) ? trimmed : null;
    }

    public static async Task<IReadOnlyList<RouteStop>?> LoadStopsAsync(string line, LineDataService data, CancellationToken ct)
    {
        try
        {
            var stops = await data.GetStopsAsync(line, ct);
            return stops.Count == 0 ? null : stops;
        }
        catch (IbbFaultException)
        {
            return null;
        }
    }

    public static IResult Invalid() =>
        Results.Problem("Line code must be 1–12 letters, digits or dashes.", statusCode: StatusCodes.Status400BadRequest);

    public static IResult Unknown(string line) =>
        Results.Problem($"No stops found for line {line}.", statusCode: StatusCodes.Status404NotFound);

    public static IResult Unavailable(LineUnavailableException e, TimeProvider clock)
    {
        var retrySeconds = e.RetryAt is { } at ? Math.Max(1, (int)(at - clock.GetUtcNow()).TotalSeconds) : 60;
        return Results.Problem(
            $"Live data unavailable ({e.Reason}). Retry in {retrySeconds} s.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?> { ["reason"] = e.Reason, ["retryAfterSeconds"] = retrySeconds });
    }

    [GeneratedRegex(@"^[A-Z0-9ÇĞİÖŞÜ\-]{1,12}$")]
    private static partial Regex Pattern();
}
