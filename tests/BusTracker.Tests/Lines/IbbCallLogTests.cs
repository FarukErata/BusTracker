using BusTracker.Api.Configuration;
using BusTracker.Api.Data;
using BusTracker.Api.Lines;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BusTracker.Tests.Lines;

public sealed class IbbCallLogTests
{
    [Fact]
    public async Task Calls_AreCountedAgainAfterARestart()
    {
        using var factory = new TestAppFactory();
        var client = await factory.CreateUserClientAsync();
        await client.GetAsync("/api/lines/131Y/live");
        await WaitUntilAsync(factory, "SELECT COUNT(*) FROM ibb_calls;", 2);

        var (gate, log) = await RestartAsync(factory);

        Assert.Equal(new FleetCallCounts(1, 1), gate.CallsInLastHour);
        await log.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RateLimitBlock_SurvivesARestart()
    {
        using var factory = new TestAppFactory();
        var running = factory.Services.GetRequiredService<FleetCallGate>();
        running.ReportRateLimited();
        await WaitUntilAsync(factory, "SELECT COUNT(*) FROM ibb_state WHERE name = 'blocked';", 1);

        var (gate, log) = await RestartAsync(factory);

        Assert.Equal(running.BlockedUntil, gate.BlockedUntil);
        Assert.Equal("rate-limited", gate.TryAcquire(FleetCallKind.Fleet).Reason);
        await log.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CallsOlderThanAnHour_AreNotRestored()
    {
        using var factory = new TestAppFactory();
        var client = await factory.CreateUserClientAsync();
        await client.GetAsync("/api/lines/131Y/live");
        await WaitUntilAsync(factory, "SELECT COUNT(*) FROM ibb_calls;", 2);

        factory.Clock.Advance(TimeSpan.FromMinutes(61));
        var (gate, log) = await RestartAsync(factory);

        Assert.Equal(new FleetCallCounts(0, 0), gate.CallsInLastHour);
        await log.StopAsync(CancellationToken.None);
    }

    private static async Task<(FleetCallGate Gate, IbbCallLog Log)> RestartAsync(TestAppFactory factory)
    {
        var options = factory.Services.GetRequiredService<IOptions<BusTrackerOptions>>();
        var gate = new FleetCallGate(options, factory.Clock);
        var log = new IbbCallLog(factory.Services.GetRequiredService<Database>(), gate, factory.Clock, NullLogger<IbbCallLog>.Instance);
        await log.StartAsync(CancellationToken.None);
        return (gate, log);
    }

    private static async Task WaitUntilAsync(TestAppFactory factory, string countSql, long expected)
    {
        var database = factory.Services.GetRequiredService<Database>();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await using var connection = await database.OpenAsync(CancellationToken.None);
            if (await connection.ExecuteScalarAsync<long>(countSql) >= expected) return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Expected {expected} from: {countSql}");
    }
}
