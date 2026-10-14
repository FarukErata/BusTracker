using BusTracker.Api.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace BusTracker.Api.Favorites;

public sealed record FavoriteLine(long Id, string LineCode, string? Direction, string? Label, DateTimeOffset CreatedAt);

public sealed record FavoriteStop(long Id, string StopCode, string StopName, string? Label, DateTimeOffset CreatedAt);

public sealed class FavoriteRepository(Database database)
{
    private const int SqliteConstraint = 19;

    public async Task<IReadOnlyList<FavoriteLine>> GetLinesAsync(long userId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        var rows = await connection.QueryAsync<LineRow>(
            """
            SELECT id AS Id, line_code AS LineCode, direction AS Direction, label AS Label, created_at AS CreatedAt
            FROM favorite_lines WHERE user_id = @userId ORDER BY id;
            """,
            new { userId });
        return rows.Select(r => new FavoriteLine(r.Id, r.LineCode, r.Direction, r.Label, DbTime.Parse(r.CreatedAt)!.Value)).ToList();
    }

    public async Task<IReadOnlyList<FavoriteStop>> GetStopsAsync(long userId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        var rows = await connection.QueryAsync<StopRow>(
            """
            SELECT id AS Id, stop_code AS StopCode, stop_name AS StopName, label AS Label, created_at AS CreatedAt
            FROM favorite_stops WHERE user_id = @userId ORDER BY id;
            """,
            new { userId });
        return rows.Select(r => new FavoriteStop(r.Id, r.StopCode, r.StopName, r.Label, DbTime.Parse(r.CreatedAt)!.Value)).ToList();
    }

    public async Task<FavoriteLine?> AddLineAsync(long userId, FavoriteLine line, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        var id = await InsertOrNullAsync(() => connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO favorite_lines (user_id, line_code, direction, label, created_at)
            VALUES (@userId, @LineCode, @Direction, @Label, @CreatedAt) RETURNING id;
            """,
            new { userId, line.LineCode, line.Direction, line.Label, CreatedAt = DbTime.Format(line.CreatedAt) }));
        return id is null ? null : line with { Id = id.Value };
    }

    public async Task<FavoriteStop?> AddStopAsync(long userId, FavoriteStop stop, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        var id = await InsertOrNullAsync(() => connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO favorite_stops (user_id, stop_code, stop_name, label, created_at)
            VALUES (@userId, @StopCode, @StopName, @Label, @CreatedAt) RETURNING id;
            """,
            new { userId, stop.StopCode, stop.StopName, stop.Label, CreatedAt = DbTime.Format(stop.CreatedAt) }));
        return id is null ? null : stop with { Id = id.Value };
    }

    public Task<bool> DeleteLineAsync(long userId, long id, CancellationToken ct) => DeleteAsync("favorite_lines", userId, id, ct);

    public Task<bool> DeleteStopAsync(long userId, long id, CancellationToken ct) => DeleteAsync("favorite_stops", userId, id, ct);

    private async Task<bool> DeleteAsync(string table, long userId, long id, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        return await connection.ExecuteAsync($"DELETE FROM {table} WHERE id = @id AND user_id = @userId;", new { id, userId }) > 0;
    }

    private static async Task<long?> InsertOrNullAsync(Func<Task<long>> insert)
    {
        try
        {
            return await insert();
        }
        catch (SqliteException e) when (e.SqliteErrorCode == SqliteConstraint)
        {
            return null;
        }
    }

    private sealed record LineRow(long Id, string LineCode, string? Direction, string? Label, string CreatedAt);

    private sealed record StopRow(long Id, string StopCode, string StopName, string? Label, string CreatedAt);
}
