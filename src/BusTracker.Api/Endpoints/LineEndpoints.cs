using BusTracker.Api.Lines;
using BusTracker.Api.Shapes;
using BusTracker.Api.Stops;

namespace BusTracker.Api.Endpoints;

public static class LineEndpoints
{
    private const int SearchLimit = 10;

    public static IEndpointRouteBuilder MapLineEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/lines", Search);
        var lines = app.MapGroup("/api/lines/{code}");
        lines.MapGet("/stops", GetStops);
        lines.MapGet("/shapes", GetShapes);
        return app;
    }

    private static async Task<IReadOnlyList<LineSummary>> Search(string? q, StopIndexRepository index, CancellationToken ct) =>
        await index.SearchLinesAsync(q ?? "", SearchLimit, ct);

    private static async Task<IResult> GetStops(string code, LineDataService data, CancellationToken ct)
    {
        if (LineCode.Normalize(code) is not { } line) return LineCode.Invalid();

        var stops = await LineCode.LoadStopsAsync(line, data, ct);
        if (stops is null) return LineCode.Unknown(line);

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

    private static async Task<IResult> GetShapes(string code, bool? all, RouteShapeRepository shapes, CancellationToken ct)
    {
        if (LineCode.Normalize(code) is not { } line) return LineCode.Invalid();

        var found = await shapes.GetForLineAsync(line, ct);
        var selected = all == true ? found : found.Where(s => s.IsMainVariant).ToList();
        return Results.Ok(new LineShapesResponse(line, selected.Select(ToShapeView).ToList()));
    }

    private static ShapeView ToShapeView(RouteShape shape) =>
        new(shape.RouteCode, shape.Direction, shape.Variant,
            Math.Round(shape.LengthMeters), Math.Round(shape.DurationSeconds),
            shape.Points.Select(p => new[] { p.Lat, p.Lng }).ToList());
}

public sealed record StopView(int Seq, string Code, string Name, double Lat, double Lng, string District);

public sealed record DirectionStops(string Direction, string Name, IReadOnlyList<StopView> Stops);

public sealed record LineStopsResponse(string Line, IReadOnlyList<DirectionStops> Directions);

public sealed record ShapeView(
    string RouteCode, string Direction, string Variant, double LengthMeters, double DurationSeconds,
    IReadOnlyList<double[]> Points);

public sealed record LineShapesResponse(string Line, IReadOnlyList<ShapeView> Shapes);
