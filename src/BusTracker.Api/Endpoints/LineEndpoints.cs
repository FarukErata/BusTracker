using System.Text.RegularExpressions;
using BusTracker.Api.Configuration;
using BusTracker.Api.Ibb;
using BusTracker.Api.Lines;
using BusTracker.Api.Shapes;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Endpoints;

public static partial class LineEndpoints
{
    public static IEndpointRouteBuilder MapLineEndpoints(this IEndpointRouteBuilder app)
    {
        var lines = app.MapGroup("/api/lines/{code}");
        lines.MapGet("/stops", GetStops);
        lines.MapGet("/live", GetLive);
        lines.MapGet("/shapes", GetShapes);
        return app;
    }

    private static async Task<IResult> GetStops(string code, LineDataService data, CancellationToken ct)
    {
        if (NormalizeLineCode(code) is not { } line) return InvalidLineCode();

        var stops = await LoadStopsAsync(line, data, ct);
        if (stops is null) return UnknownLine(line);

        var directions = stops
            .GroupBy(s => s.Direction)
            .OrderBy(g => g.Key)
            .Select(g => new DirectionStops(
                g.Key,
                g.First().DirectionName,
                g.OrderBy(s => s.Seq).Select(s => new StopView(s.Seq, s.StopCode, s.Name, s.Lat, s.Lng, s.District)).ToList()))
            .ToList();
        return Results.Ok(new LineStopsResponse(line, directions));
    }

    private static async Task<IResult> GetLive(
        string code, LineDataService data, IOptions<BusTrackerOptions> options, TimeProvider clock, CancellationToken ct)
    {
        if (NormalizeLineCode(code) is not { } line) return InvalidLineCode();

        var stops = await LoadStopsAsync(line, data, ct);
        if (stops is null) return UnknownLine(line);

        try
        {
            var live = await data.GetLiveAsync(line, ct);
            var now = clock.GetUtcNow();
            return Results.Ok(new LineLiveResponse(
                line,
                live.Snapshot.FetchedAt,
                (int)(now - live.Snapshot.FetchedAt).TotalSeconds,
                live.Stale,
                live.StaleReason,
                live.RetryAt,
                LiveLineView.Build(live.Snapshot.Buses, stops, options.Value.Tracking, now)));
        }
        catch (LineUnavailableException e)
        {
            return Unavailable(e, clock);
        }
        catch (IbbFaultException)
        {
            return Results.Ok(new LineLiveResponse(line, clock.GetUtcNow(), 0, false, null, null, []));
        }
    }

    private static async Task<IResult> GetShapes(
        string code, bool? all, RouteShapeRepository shapes, CancellationToken ct)
    {
        if (NormalizeLineCode(code) is not { } line) return InvalidLineCode();

        var found = await shapes.GetForLineAsync(line, ct);
        var selected = all == true ? found : found.Where(s => s.IsMainVariant).ToList();
        return Results.Ok(new LineShapesResponse(line, selected.Select(ToShapeView).ToList()));
    }

    private static ShapeView ToShapeView(RouteShape shape) =>
        new(shape.RouteCode, shape.Direction, shape.Variant,
            Math.Round(shape.LengthMeters), Math.Round(shape.DurationSeconds),
            shape.Points.Select(p => new[] { p.Lat, p.Lng }).ToList());

    private static async Task<IReadOnlyList<RouteStop>?> LoadStopsAsync(string line, LineDataService data, CancellationToken ct)
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

    private static string? NormalizeLineCode(string code)
    {
        var trimmed = code.Trim().ToUpperInvariant();
        return LineCodePattern().IsMatch(trimmed) ? trimmed : null;
    }

    private static IResult InvalidLineCode() =>
        Results.Problem("Line code must be 1–12 letters, digits or dashes.", statusCode: StatusCodes.Status400BadRequest);

    private static IResult UnknownLine(string line) =>
        Results.Problem($"No stops found for line {line}.", statusCode: StatusCodes.Status404NotFound);

    private static IResult Unavailable(LineUnavailableException e, TimeProvider clock)
    {
        var retrySeconds = e.RetryAt is { } at ? Math.Max(1, (int)(at - clock.GetUtcNow()).TotalSeconds) : 60;
        return Results.Problem(
            $"Live data unavailable ({e.Reason}). Retry in {retrySeconds} s.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?> { ["reason"] = e.Reason, ["retryAfterSeconds"] = retrySeconds });
    }

    [GeneratedRegex(@"^[A-Z0-9ÇĞİÖŞÜ\-]{1,12}$")]
    private static partial Regex LineCodePattern();
}

public sealed record StopView(int Seq, string Code, string Name, double Lat, double Lng, string District);

public sealed record DirectionStops(string Direction, string Name, IReadOnlyList<StopView> Stops);

public sealed record LineStopsResponse(string Line, IReadOnlyList<DirectionStops> Directions);

public sealed record ShapeView(
    string RouteCode, string Direction, string Variant, double LengthMeters, double DurationSeconds,
    IReadOnlyList<double[]> Points);

public sealed record LineShapesResponse(string Line, IReadOnlyList<ShapeView> Shapes);

public sealed record LineLiveResponse(
    string Line,
    DateTimeOffset FetchedAt,
    int AgeSeconds,
    bool Stale,
    string? StaleReason,
    DateTimeOffset? RetryAt,
    IReadOnlyList<LiveBusView> Buses);
