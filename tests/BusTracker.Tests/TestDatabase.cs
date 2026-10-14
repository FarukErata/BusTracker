using BusTracker.Api.Configuration;
using BusTracker.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BusTracker.Tests;

public sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bustracker-tests", Guid.NewGuid().ToString("N"));

    private TestDatabase(BusTrackerOptions options)
    {
        Options = options;
        Database = new Database(Microsoft.Extensions.Options.Options.Create(options),
            new HostingEnvironment { ContentRootPath = _directory });
    }

    public BusTrackerOptions Options { get; }
    public Database Database { get; }

    public static async Task<TestDatabase> CreateAsync(StaticDataOptions? staticData = null)
    {
        var db = new TestDatabase(new BusTrackerOptions
        {
            DatabasePath = "test.db",
            StaticData = staticData ?? new StaticDataOptions(),
        });
        await new DatabaseInitializer(db.Database, NullLogger<DatabaseInitializer>.Instance).StartAsync(CancellationToken.None);
        return db;
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        return ValueTask.CompletedTask;
    }
}
