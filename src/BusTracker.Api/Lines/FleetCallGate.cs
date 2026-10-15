using BusTracker.Api.Configuration;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Lines;

public enum FleetCallKind { Fleet, Roster }

public sealed record FleetCallCounts(int Fleet, int Roster)
{
    public int Total => Fleet + Roster;
}

public sealed class FleetCallGate(IOptions<BusTrackerOptions> options, TimeProvider clock)
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private readonly PollingOptions _polling = options.Value.Polling;
    private readonly Queue<(DateTimeOffset At, FleetCallKind Kind)> _calls = new();
    private readonly Lock _lock = new();
    private int _backoffLevel = -1;
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;

    public DateTimeOffset? BlockedUntil
    {
        get
        {
            lock (_lock) return _blockedUntil > clock.GetUtcNow() ? _blockedUntil : null;
        }
    }

    public FleetCallCounts CallsInLastHour
    {
        get
        {
            lock (_lock)
            {
                DropExpired(clock.GetUtcNow());
                return new FleetCallCounts(Count(FleetCallKind.Fleet), Count(FleetCallKind.Roster));
            }
        }
    }

    public GateDecision TryAcquire(FleetCallKind kind)
    {
        lock (_lock)
        {
            var now = clock.GetUtcNow();
            if (now < _blockedUntil) return GateDecision.Deny("rate-limited", _blockedUntil);

            DropExpired(now);
            if (_calls.Count >= _polling.CallsPerHour)
                return GateDecision.Deny("call-budget", _calls.Peek().At + Hour);
            if (kind == FleetCallKind.Roster && RosterLimitReached(now) is { } retryAt)
                return GateDecision.Deny("call-budget", retryAt);

            _calls.Enqueue((now, kind));
            return GateDecision.Allow;
        }
    }

    public void ReportSuccess()
    {
        lock (_lock) _backoffLevel = -1;
    }

    public void ReportRateLimited()
    {
        lock (_lock)
        {
            _backoffLevel = Math.Min(_backoffLevel + 1, _polling.BackoffMinutes.Length - 1);
            _blockedUntil = clock.GetUtcNow() + TimeSpan.FromMinutes(_polling.BackoffMinutes[_backoffLevel]);
        }
    }

    private DateTimeOffset? RosterLimitReached(DateTimeOffset now)
    {
        var rosters = _calls.Where(c => c.Kind == FleetCallKind.Roster).Select(c => c.At).ToList();
        if (rosters.Count >= _polling.RosterCallsPerHour) return rosters[0] + Hour;

        var lastMinute = rosters.Where(at => now - at < Minute).ToList();
        return lastMinute.Count >= _polling.RosterCallsPerMinute ? lastMinute[0] + Minute : null;
    }

    private int Count(FleetCallKind kind) => _calls.Count(c => c.Kind == kind);

    private void DropExpired(DateTimeOffset now)
    {
        while (_calls.Count > 0 && now - _calls.Peek().At >= Hour)
            _calls.Dequeue();
    }
}

public sealed record GateDecision(bool Allowed, string? Reason, DateTimeOffset? RetryAt)
{
    public static readonly GateDecision Allow = new(true, null, null);

    public static GateDecision Deny(string reason, DateTimeOffset retryAt) => new(false, reason, retryAt);
}
