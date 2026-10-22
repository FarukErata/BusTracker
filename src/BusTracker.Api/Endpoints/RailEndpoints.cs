using BusTracker.Api.Rail;

namespace BusTracker.Api.Endpoints;

public static class RailEndpoints
{
    private const int SearchLimit = 15;

    public static IEndpointRouteBuilder MapRailEndpoints(this IEndpointRouteBuilder app)
    {
        var stations = app.MapGroup("/api/rail/stations");
        stations.MapGet("/", Search);
        stations.MapGet("/all", All);
        stations.MapGet("/{id:int}", GetStation);
        var lines = app.MapGroup("/api/rail/lines");
        lines.MapGet("/", SearchLines);
        lines.MapGet("/{id:int}", GetLine);
        return app;
    }

    private static IReadOnlyList<RailStationView> Search(string? q, RailCatalog catalog, RailBoard board) =>
        catalog.Search(q ?? "", SearchLimit).Select(board.View).ToList();

    private static IReadOnlyList<RailLineView> SearchLines(string? q, RailCatalog catalog) =>
        catalog.SearchLines(q ?? "").Select(RailBoard.View).ToList();

    private static IResult GetLine(int id, RailBoard board) =>
        board.Line(id) is { } line
            ? Results.Ok(line)
            : Results.Problem($"Rail line {id} is not known.", statusCode: StatusCodes.Status404NotFound);

    private static IReadOnlyList<RailStationView> All(RailCatalog catalog, RailBoard board) =>
        catalog.Data.Stations.Values.Where(s => s.Lat is not null && s.Lng is not null).OrderBy(s => s.Line).ThenBy(s => s.Order).Select(board.View).ToList();

    private static async Task<IResult> GetStation(int id, RailCatalog catalog, RailBoard board, CancellationToken ct)
    {
        if (catalog.Data.Stations.Count == 0)
            return Results.Problem("Rail stations aren't loaded yet. Try again in a minute.", statusCode: StatusCodes.Status503ServiceUnavailable);
        return await board.StationAsync(id, ct) is { } station
            ? Results.Ok(station)
            : Results.Problem($"Station {id} is not known.", statusCode: StatusCodes.Status404NotFound);
    }
}
