using System.Security.Claims;
using BusTracker.Api.Favorites;
using BusTracker.Api.Ibb;
using BusTracker.Api.Lines;
using BusTracker.Api.Rail;
using BusTracker.Api.Stops;
using BusTracker.Api.Timetables;
using BusTracker.Api.Users;

namespace BusTracker.Api.Endpoints;

public static class FavoriteEndpoints
{
    private const int MaxLabelLength = 60;

    public static IEndpointRouteBuilder MapFavoriteEndpoints(this IEndpointRouteBuilder app)
    {
        var favorites = app.MapGroup("/api/favorites");
        favorites.MapGet("/", GetAll);
        favorites.MapPost("/lines", AddLine);
        favorites.MapDelete("/lines/{id:long}", DeleteLine);
        favorites.MapPost("/stops", AddStop);
        favorites.MapDelete("/stops/{id:long}", DeleteStop);
        favorites.MapPost("/stations", AddStation);
        favorites.MapDelete("/stations/{id:long}", DeleteStation);
        return app;
    }

    private static async Task<FavoritesResponse> GetAll(
        ClaimsPrincipal user, FavoriteRepository favorites, StopIndexRepository stopIndex, LineDataService data, LinePlans plans,
        FleetFeed fleet, TimetableService timetables, RailCatalog rail, RailBoard board, CancellationToken ct)
    {
        fleet.Warm();
        var lines = new List<FavoriteLineView>();
        foreach (var line in await favorites.GetLinesAsync(user.UserId(), ct))
            lines.Add(new FavoriteLineView(line, await DirectionNamesAsync(line.LineCode, data, ct)));

        var stops = new List<FavoriteStopView>();
        foreach (var stop in await favorites.GetStopsAsync(user.UserId(), ct))
            stops.Add(new FavoriteStopView(stop, await stopIndex.GetStopAsync(stop.StopCode, ct),
                (await stopIndex.GetLinesAtStopAsync(stop.StopCode, ct)).Select(l => l.LineCode).Distinct().ToList()));

        var watched = lines.Select(l => l.Favorite.LineCode).Concat(stops.SelectMany(s => s.Lines)).ToList();
        timetables.Warm(watched);
        var stations = new List<FavoriteStationView>();
        foreach (var favorite in await favorites.GetStationsAsync(user.UserId(), ct))
            stations.Add(rail.Station(favorite.StationId) is { } station
                ? new FavoriteStationView(favorite, board.View(station), await board.SummaryAsync(station, ct))
                : new FavoriteStationView(favorite, null, []));
        return new FavoritesResponse(lines, stops, plans.AnnouncementsFor(watched), stations);
    }

    private static async Task<IResult> AddLine(
        AddFavoriteLineRequest request, ClaimsPrincipal user, FavoriteRepository favorites, LineDataService data,
        TimeProvider clock, CancellationToken ct)
    {
        if (LineCode.Normalize(request.Line ?? "") is not { } line) return LineCode.Invalid();
        if (await LineCode.LoadStopsAsync(line, data, ct) is not { } stops) return LineCode.Unknown(line);

        var direction = NormalizeDirection(request.Direction);
        if (!HasDirection(stops, direction))
            return Results.Problem($"Line {line} has no direction {direction}.", statusCode: StatusCodes.Status404NotFound);

        var added = await favorites.AddLineAsync(user.UserId(), new FavoriteLine(0, line, direction, Label(request.Label), clock.GetUtcNow()), ct);
        return added is null ? AlreadyFavorite() : Results.Created($"/api/favorites/lines/{added.Id}", added);
    }

    private static string? NormalizeDirection(string? direction) =>
        string.IsNullOrWhiteSpace(direction) ? null : direction.Trim().ToUpperInvariant();

    private static bool HasDirection(IReadOnlyList<RouteStop> stops, string? direction) =>
        direction is null || stops.Any(s => s.Direction == direction);

    private static async Task<IResult> AddStop(
        AddFavoriteStopRequest request, ClaimsPrincipal user, FavoriteRepository favorites, StopIndexRepository stopIndex,
        TimeProvider clock, CancellationToken ct)
    {
        if (await stopIndex.GetStopAsync((request.Stop ?? "").Trim(), ct) is not { } stop)
            return Results.Problem($"Stop {request.Stop} is not in the stop index.", statusCode: StatusCodes.Status404NotFound);

        var added = await favorites.AddStopAsync(user.UserId(), new FavoriteStop(0, stop.Code, stop.Name, Label(request.Label), clock.GetUtcNow()), ct);
        return added is null ? AlreadyFavorite() : Results.Created($"/api/favorites/stops/{added.Id}", added);
    }

    private static async Task<IResult> AddStation(
        AddFavoriteStationRequest request, ClaimsPrincipal user, FavoriteRepository favorites, RailCatalog rail, TimeProvider clock, CancellationToken ct)
    {
        if (rail.Station(request.Station) is not { } station)
            return Results.Problem($"Station {request.Station} is not known.", statusCode: StatusCodes.Status404NotFound);

        var added = await favorites.AddStationAsync(user.UserId(), new FavoriteStation(0, station.Id, station.Name, station.Line, clock.GetUtcNow()), ct);
        return added is null ? AlreadyFavorite() : Results.Created($"/api/favorites/stations/{added.Id}", added);
    }

    private static async Task<IResult> DeleteStation(long id, ClaimsPrincipal user, FavoriteRepository favorites, CancellationToken ct) =>
        await favorites.DeleteStationAsync(user.UserId(), id, ct) ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> DeleteLine(long id, ClaimsPrincipal user, FavoriteRepository favorites, CancellationToken ct) =>
        await favorites.DeleteLineAsync(user.UserId(), id, ct) ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> DeleteStop(long id, ClaimsPrincipal user, FavoriteRepository favorites, CancellationToken ct) =>
        await favorites.DeleteStopAsync(user.UserId(), id, ct) ? Results.NoContent() : Results.NotFound();

    private static async Task<IReadOnlyDictionary<string, string>> DirectionNamesAsync(string line, LineDataService data, CancellationToken ct) =>
        await LineCode.LoadStopsAsync(line, data, ct) is { } stops
            ? stops.GroupBy(s => s.Direction).ToDictionary(g => g.Key, g => $"{g.MinBy(s => s.Seq)!.Name} → {g.MaxBy(s => s.Seq)!.Name}")
            : new Dictionary<string, string>();

    private static string? Label(string? label) =>
        string.IsNullOrWhiteSpace(label) ? null : label.Trim()[..Math.Min(MaxLabelLength, label.Trim().Length)];

    private static IResult AlreadyFavorite() =>
        Results.Problem("This is already a favorite.", statusCode: StatusCodes.Status409Conflict);
}

public sealed record AddFavoriteLineRequest(string? Line, string? Direction, string? Label);

public sealed record AddFavoriteStopRequest(string? Stop, string? Label);

public sealed record AddFavoriteStationRequest(int Station);

public sealed record FavoriteStationView(FavoriteStation Favorite, RailStationView? Station, IReadOnlyList<RailDirectionSummary> Directions);

public sealed record FavoriteLineView(FavoriteLine Favorite, IReadOnlyDictionary<string, string> Directions);

public sealed record FavoriteStopView(FavoriteStop Favorite, StopInfo? Stop, IReadOnlyList<string> Lines);

public sealed record FavoritesResponse(
    IReadOnlyList<FavoriteLineView> Lines, IReadOnlyList<FavoriteStopView> Stops, IReadOnlyList<AnnouncementView> Announcements,
    IReadOnlyList<FavoriteStationView> Stations);
