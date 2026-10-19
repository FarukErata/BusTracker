using BusTracker.Api.Configuration;
using BusTracker.Api.Lines;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BusTracker.Tests.Lines;

public sealed class FleetCallGateTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero));

    private FleetCallGate CreateGate(int perHour = 90, int rostersPerHour = 30, int rostersPerMinute = 4, int fleetReserved = 0) =>
        new(Options.Create(new BusTrackerOptions
        {
            Polling = new PollingOptions
            {
                CallsPerHour = perHour,
                RosterCallsPerHour = rostersPerHour,
                RosterCallsPerMinute = rostersPerMinute,
                FleetReservedCalls = fleetReserved,
                BackoffMinutes = [5, 10, 20],
            },
        }), _clock);

    [Fact]
    public void HourlyBudget_DeniesEveryCallOnceSpent()
    {
        var gate = CreateGate(perHour: 3);

        Assert.All(Enumerable.Range(0, 3), _ => Assert.True(gate.TryAcquire(FleetCallKind.Fleet).Allowed));
        var denied = gate.TryAcquire(FleetCallKind.Fleet);

        Assert.False(denied.Allowed);
        Assert.Equal("call-budget", denied.Reason);
        Assert.Equal(_clock.GetUtcNow().AddHours(1), denied.RetryAt);
        Assert.False(gate.TryAcquire(FleetCallKind.Roster).Allowed);
    }

    [Fact]
    public void HourlyBudget_FreesUpAfterOneHour()
    {
        var gate = CreateGate(perHour: 1);
        gate.TryAcquire(FleetCallKind.Fleet);

        _clock.Advance(TimeSpan.FromMinutes(59));
        Assert.False(gate.TryAcquire(FleetCallKind.Fleet).Allowed);

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(gate.TryAcquire(FleetCallKind.Fleet).Allowed);
    }

    [Fact]
    public void Rosters_AreCappedPerMinuteWithoutBlockingTheFleet()
    {
        var gate = CreateGate(rostersPerMinute: 2);

        Assert.True(gate.TryAcquire(FleetCallKind.Roster).Allowed);
        Assert.True(gate.TryAcquire(FleetCallKind.Roster).Allowed);
        var denied = gate.TryAcquire(FleetCallKind.Roster);

        Assert.False(denied.Allowed);
        Assert.Equal(_clock.GetUtcNow().AddMinutes(1), denied.RetryAt);
        Assert.True(gate.TryAcquire(FleetCallKind.Fleet).Allowed);

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(gate.TryAcquire(FleetCallKind.Roster).Allowed);
    }

    [Fact]
    public void Rosters_AreCappedPerHour()
    {
        var gate = CreateGate(rostersPerHour: 2, rostersPerMinute: 10);
        gate.TryAcquire(FleetCallKind.Roster);
        gate.TryAcquire(FleetCallKind.Roster);

        _clock.Advance(TimeSpan.FromMinutes(30));

        Assert.False(gate.TryAcquire(FleetCallKind.Roster).Allowed);
        Assert.True(gate.TryAcquire(FleetCallKind.Fleet).Allowed);
        Assert.Equal(new FleetCallCounts(1, 2), gate.CallsInLastHour);
    }

    [Fact]
    public void FleetReserve_KeepsRostersFromUsingTheFleetsShare()
    {
        var gate = CreateGate(perHour: 6, rostersPerHour: 10, rostersPerMinute: 10, fleetReserved: 4);

        Assert.True(gate.TryAcquire(FleetCallKind.Roster).Allowed);
        Assert.True(gate.TryAcquire(FleetCallKind.Roster).Allowed);
        var denied = gate.TryAcquire(FleetCallKind.Roster);

        Assert.False(denied.Allowed);
        Assert.Equal(_clock.GetUtcNow().AddHours(1), denied.RetryAt);
        Assert.All(Enumerable.Range(0, 4), _ => Assert.True(gate.TryAcquire(FleetCallKind.Fleet).Allowed));
        Assert.False(gate.TryAcquire(FleetCallKind.Fleet).Allowed);
    }

    [Fact]
    public void FleetReserve_LeavesRostersAloneWhileTheFleetIsBusy()
    {
        var gate = CreateGate(perHour: 6, rostersPerHour: 10, rostersPerMinute: 10, fleetReserved: 4);

        Assert.All(Enumerable.Range(0, 4), _ => Assert.True(gate.TryAcquire(FleetCallKind.Fleet).Allowed));

        Assert.True(gate.TryAcquire(FleetCallKind.Roster).Allowed);
        Assert.True(gate.TryAcquire(FleetCallKind.Roster).Allowed);
    }

    [Fact]
    public void RateLimit_BacksOffWithGrowingSteps()
    {
        var gate = CreateGate();
        var start = _clock.GetUtcNow();

        gate.ReportRateLimited();
        Assert.Equal(start.AddMinutes(5), gate.BlockedUntil);
        Assert.Equal("rate-limited", gate.TryAcquire(FleetCallKind.Fleet).Reason);

        _clock.Advance(TimeSpan.FromMinutes(5));
        gate.ReportRateLimited();
        Assert.Equal(start.AddMinutes(5 + 10), gate.BlockedUntil);

        _clock.Advance(TimeSpan.FromMinutes(10));
        gate.ReportRateLimited();
        _clock.Advance(TimeSpan.FromMinutes(20));
        gate.ReportRateLimited();
        Assert.Equal(_clock.GetUtcNow().AddMinutes(20), gate.BlockedUntil);
    }

    [Fact]
    public void Success_ResetsBackoffLevel()
    {
        var gate = CreateGate();
        gate.ReportRateLimited();
        _clock.Advance(TimeSpan.FromMinutes(5));

        gate.ReportSuccess();
        gate.ReportRateLimited();

        Assert.Equal(_clock.GetUtcNow().AddMinutes(5), gate.BlockedUntil);
    }

    [Fact]
    public void BlockedUntil_IsNullOnceBackoffExpires()
    {
        var gate = CreateGate();
        gate.ReportRateLimited();

        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Null(gate.BlockedUntil);
        Assert.True(gate.TryAcquire(FleetCallKind.Fleet).Allowed);
    }
}
