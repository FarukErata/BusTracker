using BusTracker.Api.Configuration;
using BusTracker.Api.Tracing;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Data;

public sealed class Database
{
    private readonly string _connectionString;

    public Database(IOptions<BusTrackerOptions> options, IHostEnvironment environment)
    {
        var path = Path.IsPathRooted(options.Value.DatabasePath)
            ? options.Value.DatabasePath
            : Path.Combine(environment.ContentRootPath, options.Value.DatabasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FilePath = path;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true, ForeignKeys = true }.ToString();
    }

    public string FilePath { get; }

    public async Task<TracedConnection> OpenAsync(CancellationToken ct)
    {
        using var span = Telemetry.Span("db", "open");
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(ct);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
        return new TracedConnection(connection);
    }
}

public sealed class DatabaseInitializer(Database database, ILogger<DatabaseInitializer> logger) : IHostedService
{
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE route_shapes (
            route_code   TEXT PRIMARY KEY,
            line_code    TEXT NOT NULL,
            direction    TEXT NOT NULL,
            variant      TEXT NOT NULL,
            length_m     REAL NOT NULL,
            duration_s   REAL NOT NULL,
            point_count  INTEGER NOT NULL,
            polyline     TEXT NOT NULL
        );
        CREATE INDEX ix_route_shapes_line ON route_shapes (line_code);

        CREATE TABLE data_sources (
            name           TEXT PRIMARY KEY,
            etag           TEXT,
            last_modified  TEXT,
            size           INTEGER,
            checked_at     TEXT NOT NULL,
            imported_at    TEXT,
            item_count     INTEGER
        );

        CREATE TABLE stops (
            stop_code   TEXT PRIMARY KEY,
            name        TEXT NOT NULL,
            search_name TEXT NOT NULL,
            district    TEXT NOT NULL,
            lat         REAL NOT NULL,
            lng         REAL NOT NULL,
            towards     TEXT
        );

        CREATE TABLE stop_lines (
            stop_code      TEXT NOT NULL,
            line_code      TEXT NOT NULL,
            direction      TEXT NOT NULL,
            direction_name TEXT NOT NULL,
            seq            INTEGER NOT NULL,
            PRIMARY KEY (line_code, direction, seq)
        );
        CREATE INDEX ix_stop_lines_stop ON stop_lines (stop_code);

        CREATE TABLE users (
            id          INTEGER PRIMARY KEY,
            created_at  TEXT NOT NULL,
            invite_code TEXT
        );

        CREATE TABLE devices (
            id           INTEGER PRIMARY KEY,
            user_id      INTEGER NOT NULL REFERENCES users (id) ON DELETE CASCADE,
            key_hash     TEXT NOT NULL UNIQUE,
            platform     TEXT NOT NULL,
            name         TEXT NOT NULL,
            created_at   TEXT NOT NULL,
            last_seen_at TEXT NOT NULL
        );
        CREATE INDEX ix_devices_user ON devices (user_id);

        CREATE TABLE link_codes (
            code_hash  TEXT PRIMARY KEY,
            user_id    INTEGER NOT NULL REFERENCES users (id) ON DELETE CASCADE,
            expires_at TEXT NOT NULL
        );

        CREATE TABLE favorite_lines (
            id         INTEGER PRIMARY KEY,
            user_id    INTEGER NOT NULL REFERENCES users (id) ON DELETE CASCADE,
            line_code  TEXT NOT NULL,
            direction  TEXT,
            label      TEXT,
            created_at TEXT NOT NULL
        );
        CREATE UNIQUE INDEX ux_favorite_lines ON favorite_lines (user_id, line_code, COALESCE(direction, ''));

        CREATE TABLE favorite_stops (
            id         INTEGER PRIMARY KEY,
            user_id    INTEGER NOT NULL REFERENCES users (id) ON DELETE CASCADE,
            stop_code  TEXT NOT NULL,
            stop_name  TEXT NOT NULL,
            label      TEXT,
            created_at TEXT NOT NULL,
            UNIQUE (user_id, stop_code)
        );
        """,
        """
        CREATE TABLE bus_lines (
            door_no      TEXT PRIMARY KEY,
            line_code    TEXT NOT NULL,
            route_code   TEXT NOT NULL,
            destination  TEXT NOT NULL,
            confirmed_at TEXT NOT NULL
        );
        CREATE INDEX ix_bus_lines_line ON bus_lines (line_code);
        """,
        """
        CREATE TABLE ibb_calls (
            at   TEXT NOT NULL,
            kind TEXT NOT NULL
        );
        CREATE INDEX ix_ibb_calls_at ON ibb_calls (at);

        CREATE TABLE ibb_state (
            name  TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE hop_times (
            route_code  TEXT NOT NULL,
            from_stop   INTEGER NOT NULL,
            observed_at TEXT NOT NULL,
            seconds     REAL NOT NULL
        );
        CREATE INDEX ix_hop_times_route ON hop_times (route_code, from_stop, observed_at);
        CREATE INDEX ix_hop_times_at ON hop_times (observed_at);
        """,
        """
        CREATE TABLE eta_checks (
            route_code   TEXT NOT NULL,
            stop_index   INTEGER NOT NULL,
            predicted_at TEXT NOT NULL,
            actual_s     REAL NOT NULL,
            eta_speed    INTEGER NOT NULL,
            eta_hops     INTEGER NOT NULL,
            distance_m   REAL NOT NULL,
            age_s        INTEGER NOT NULL
        );
        CREATE INDEX ix_eta_checks_at ON eta_checks (predicted_at);
        """,
    ];

    public async Task StartAsync(CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await connection.ExecuteAsync("PRAGMA journal_mode = WAL;");
        var version = await connection.ExecuteScalarAsync<int>("PRAGMA user_version;");

        for (var i = version; i < Migrations.Length; i++)
        {
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await connection.ExecuteAsync(Migrations[i], transaction: transaction);
            await connection.ExecuteAsync($"PRAGMA user_version = {i + 1};", transaction: transaction);
            await transaction.CommitAsync(ct);
            logger.LogInformation("Applied database migration {Version}", i + 1);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
