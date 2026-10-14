using BusTracker.Api.Configuration;
using BusTracker.Api.Lines;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BusTracker.Tests.Lines;

public sealed class FleetCallGateTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero));

    private FleetCallGate CreateGate(int callsPerMinute = 3) =>
        new(Options.Create(new BusTrackerOptions
        {
            Polling = new PollingOptions { FleetCallsPerMinute = callsPerMinute, BackoffMinutes = [2, 4, 8] },
        }), _clock);

    [Fact]
    public void Budget_DeniesCallsBeyondLimitWithinOneMinute()
    {
        var gate = CreateGate(callsPerMinute: 3);

        Assert.All(Enumerable.Range(0, 3), _ => Assert.True(gate.TryAcquire().Allowed));
        var denied = gate.TryAcquire();

        Assert.False(denied.Allowed);
        Assert.Equal("call-budget", denied.Reason);
        Assert.Equal(_clock.GetUtcNow().AddMinutes(1), denied.RetryAt);
    }

    [Fact]
    public void Budget_FreesUpAfterOneMinute()
    {
        var gate = CreateGate(callsPerMinute: 1);
        gate.TryAcquire();

        _clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(gate.TryAcquire().Allowed);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(gate.TryAcquire().Allowed);
    }

    [Fact]
    public void RateLimit_BacksOffWithGrowingSteps()
    {
        var gate = CreateGate();
        var start = _clock.GetUtcNow();

        gate.ReportRateLimited();
        Assert.Equal(start.AddMinutes(2), gate.BlockedUntil);
        Assert.Equal("rate-limited", gate.TryAcquire().Reason);

        _clock.Advance(TimeSpan.FromMinutes(2));
        gate.ReportRateLimited();
        Assert.Equal(start.AddMinutes(2 + 4), gate.BlockedUntil);

        _clock.Advance(TimeSpan.FromMinutes(4));
        gate.ReportRateLimited();
        _clock.Advance(TimeSpan.FromMinutes(8));
        gate.ReportRateLimited();
        Assert.Equal(_clock.GetUtcNow().AddMinutes(8), gate.BlockedUntil);
    }

    [Fact]
    public void Success_ResetsBackoffLevel()
    {
        var gate = CreateGate();
        gate.ReportRateLimited();
        _clock.Advance(TimeSpan.FromMinutes(2));

        gate.ReportSuccess();
        gate.ReportRateLimited();

        Assert.Equal(_clock.GetUtcNow().AddMinutes(2), gate.BlockedUntil);
    }

    [Fact]
    public void BlockedUntil_IsNullOnceBackoffExpires()
    {
        var gate = CreateGate();
        gate.ReportRateLimited();

        _clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Null(gate.BlockedUntil);
        Assert.True(gate.TryAcquire().Allowed);
    }
}
