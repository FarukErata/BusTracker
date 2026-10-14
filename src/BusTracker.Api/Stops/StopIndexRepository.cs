using BusTracker.Api.Data;
using BusTracker.Api.Ibb;
using Dapper;

namespace BusTracker.Api.Stops;

public sealed record StopInfo(string Code, string Name, string District, double Lat, double Lng);

public sealed record StopLine(string LineCode, string Direction, string DirectionName, int Seq);

public sealed class StopIndexRepository(Database database)
{
    public const string SourceName = "stop-index";

    public async Task<int> ReplaceAllAsync(IAsyncEnumerable<RouteStop> rows, int minimumCount, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync("DELETE FROM stop_lines; DELETE FROM stops;", transaction: transaction);

        var count = 0;
        await foreach (var row in rows.WithCancellation(ct))
        {
            await connection.ExecuteAsync(
                """
                INSERT OR IGNORE INTO stops (stop_code, name, search_name, district, lat, lng)
                VALUES (@StopCode, @Name, @SearchName, @District, @Lat, @Lng);
                INSERT OR REPLACE INTO stop_lines (stop_code, line_code, direction, direction_name, seq)
                VALUES (@StopCode, @LineCode, @Direction, @DirectionName, @Seq);
                """,
                new
                {
                    row.StopCode, row.Name, SearchName = SearchText.Normalize(row.Name), row.District, row.Lat, row.Lng,
                    row.LineCode, row.Direction, row.DirectionName, row.Seq,
                },
                transaction);
            count++;
        }

        if (count < minimumCount)
            throw new InvalidDataException($"Only {count} stop rows in the new index, expected at least {minimumCount}");

        await connection.ExecuteAsync(
            """
            INSERT INTO data_sources (name, checked_at, imported_at, item_count) VALUES (@SourceName, @Now, @Now, @count)
            ON CONFLICT (name) DO UPDATE SET checked_at = excluded.checked_at, imported_at = excluded.imported_at, item_count = excluded.item_count;
            """,
            new { SourceName, Now = DbTime.Format(now), count }, transaction);
        await transaction.CommitAsync(ct);
        return count;
    }

    public async Task<(DateTimeOffset? ImportedAt, int? Count)> GetStateAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<(string? ImportedAt, long? ItemCount)>(
            "SELECT imported_at, item_count FROM data_sources WHERE name = @SourceName;", new { SourceName });
        return (DbTime.Parse(row.ImportedAt), (int?)row.ItemCount);
    }

    public async Task<StopInfo?> GetStopAsync(string stopCode, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<StopInfo>(
            $"{SelectStop} WHERE stop_code = @stopCode;", new { stopCode });
    }

    public async Task<IReadOnlyList<StopLine>> GetLinesAtStopAsync(string stopCode, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        var rows = await connection.QueryAsync<StopLineRow>(
            """
            SELECT line_code AS LineCode, direction AS Direction, direction_name AS DirectionName, MIN(seq) AS Seq
            FROM stop_lines WHERE stop_code = @stopCode
            GROUP BY line_code, direction ORDER BY line_code, direction;
            """,
            new { stopCode });
        return rows.Select(r => new StopLine(r.LineCode, r.Direction, r.DirectionName, (int)r.Seq)).ToList();
    }

    public async Task<IReadOnlyList<RouteStop>> GetRouteStopsAsync(string lineCode, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        var rows = await connection.QueryAsync<RouteStopRow>(
            """
            SELECT l.line_code AS LineCode, l.direction AS Direction, l.direction_name AS DirectionName, l.seq AS Seq,
                   s.stop_code AS StopCode, s.name AS Name, s.lat AS Lat, s.lng AS Lng, s.district AS District
            FROM stop_lines l JOIN stops s ON s.stop_code = l.stop_code
            WHERE l.line_code = @lineCode ORDER BY l.direction, l.seq;
            """,
            new { lineCode });
        return rows.Select(r => new RouteStop(r.LineCode, r.Direction, r.DirectionName, (int)r.Seq, r.StopCode, r.Name, r.Lat, r.Lng, r.District)).ToList();
    }

    public async Task<IReadOnlyList<(StopInfo Stop, IReadOnlyList<string> Lines)>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var normalized = SearchText.Normalize(query);
        if (normalized.Length < 2) return [];

        await using var connection = await database.OpenAsync(ct);
        var stops = (await connection.QueryAsync<StopInfo>(
            $"""
            {SelectStop}
            WHERE search_name LIKE @Contains ESCAPE '\' OR stop_code = @Exact
            ORDER BY CASE WHEN search_name LIKE @Starts ESCAPE '\' THEN 0 ELSE 1 END, name
            LIMIT @limit;
            """,
            new { Contains = $"%{EscapeLike(normalized)}%", Starts = $"{EscapeLike(normalized)}%", Exact = query.Trim(), limit })).ToList();

        var result = new List<(StopInfo, IReadOnlyList<string>)>();
        foreach (var stop in stops)
        {
            var lines = await connection.QueryAsync<string>(
                "SELECT DISTINCT line_code FROM stop_lines WHERE stop_code = @Code ORDER BY line_code;", new { stop.Code });
            result.Add((stop, lines.ToList()));
        }
        return result;
    }

    private const string SelectStop =
        "SELECT stop_code AS Code, name AS Name, district AS District, lat AS Lat, lng AS Lng FROM stops";

    private sealed record StopLineRow(string LineCode, string Direction, string DirectionName, long Seq);

    private sealed record RouteStopRow(
        string LineCode, string Direction, string DirectionName, long Seq, string StopCode, string Name, double Lat, double Lng, string District);

    private static string EscapeLike(string text) => text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
