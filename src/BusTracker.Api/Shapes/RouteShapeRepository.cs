using System.Globalization;
using BusTracker.Api.Data;
using Dapper;

namespace BusTracker.Api.Shapes;

public sealed record DataSourceState(
    string Name,
    string? ETag,
    DateTimeOffset? LastModified,
    long? Size,
    DateTimeOffset CheckedAt,
    DateTimeOffset? ImportedAt,
    int? ItemCount)
{
    public SourceVersion Version => new(ETag, LastModified, Size);
}

public sealed class RouteShapeRepository(Database database)
{
    public const string SourceName = "route-shapes";
    private const int ImportBatch = 500;

    public async Task<int> ReplaceAllAsync(IAsyncEnumerable<RouteShape> shapes, int minimumCount, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync("DELETE FROM route_shapes;", transaction: transaction);

        var count = 0;
        await foreach (var chunk in shapes.Chunk(ImportBatch).WithCancellation(ct))
        {
            await connection.ExecuteAsync(
                """
                INSERT OR REPLACE INTO route_shapes
                    (route_code, line_code, direction, variant, length_m, duration_s, point_count, polyline)
                VALUES (@RouteCode, @LineCode, @Direction, @Variant, @LengthMeters, @DurationSeconds, @PointCount, @Polyline);
                """,
                chunk.Select(shape => new
                {
                    shape.RouteCode, shape.LineCode, shape.Direction, shape.Variant,
                    shape.LengthMeters, shape.DurationSeconds,
                    PointCount = shape.Points.Count,
                    Polyline = PolylineCodec.Encode(shape.Points),
                }),
                transaction);
            count += chunk.Length;
        }

        if (count < minimumCount)
            throw new InvalidDataException($"Only {count} route shapes in the new file, expected at least {minimumCount}");

        await transaction.CommitAsync(ct);
        return count;
    }

    public async Task<IReadOnlyList<RouteShape>> GetForLineAsync(string lineCode, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        var rows = await connection.QueryAsync<ShapeRow>(
            """
            SELECT route_code AS RouteCode, line_code AS LineCode, direction AS Direction, variant AS Variant,
                   length_m AS LengthMeters, duration_s AS DurationSeconds, polyline AS Polyline
            FROM route_shapes WHERE line_code = @lineCode ORDER BY direction, variant;
            """,
            new { lineCode });
        return rows.Select(r => new RouteShape(
            r.RouteCode, r.LineCode, r.Direction, r.Variant, r.LengthMeters, r.DurationSeconds,
            PolylineCodec.Decode(r.Polyline))).ToList();
    }

    public async Task<int> CountAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM route_shapes;");
    }

    public async Task<DataSourceState?> GetSourceStateAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<SourceRow>(
            """
            SELECT name AS Name, etag AS ETag, last_modified AS LastModified, size AS Size,
                   checked_at AS CheckedAt, imported_at AS ImportedAt, item_count AS ItemCount
            FROM data_sources WHERE name = @SourceName;
            """,
            new { SourceName });
        return row is null
            ? null
            : new DataSourceState(row.Name, row.ETag, ParseTime(row.LastModified), row.Size,
                ParseTime(row.CheckedAt)!.Value, ParseTime(row.ImportedAt), (int?)row.ItemCount);
    }

    public async Task SaveSourceStateAsync(DataSourceState state, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO data_sources (name, etag, last_modified, size, checked_at, imported_at, item_count)
            VALUES (@Name, @ETag, @LastModified, @Size, @CheckedAt, @ImportedAt, @ItemCount)
            ON CONFLICT (name) DO UPDATE SET
                etag = excluded.etag, last_modified = excluded.last_modified, size = excluded.size,
                checked_at = excluded.checked_at, imported_at = excluded.imported_at, item_count = excluded.item_count;
            """,
            new
            {
                state.Name, state.ETag, state.Size, state.ItemCount,
                LastModified = FormatTime(state.LastModified),
                CheckedAt = FormatTime(state.CheckedAt),
                ImportedAt = FormatTime(state.ImportedAt),
            });
    }

    private static string? FormatTime(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTime(string? value) =>
        value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private sealed record ShapeRow(
        string RouteCode, string LineCode, string Direction, string Variant,
        double LengthMeters, double DurationSeconds, string Polyline);

    private sealed record SourceRow(
        string Name, string? ETag, string? LastModified, long? Size,
        string CheckedAt, string? ImportedAt, long? ItemCount);
}
