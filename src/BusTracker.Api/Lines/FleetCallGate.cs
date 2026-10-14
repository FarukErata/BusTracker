using BusTracker.Api.Configuration;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Lines;

public sealed class FleetCallGate(IOptions<BusTrackerOptions> options, TimeProvider clock)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly PollingOptions _polling = options.Value.Polling;
    private readonly Queue<DateTimeOffset> _recentCalls = new();
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

    public int CallsInLastMinute
    {
        get
        {
            lock (_lock)
            {
                DropExpired(clock.GetUtcNow());
                return _recentCalls.Count;
            }
        }
    }

    public GateDecision TryAcquire()
    {
        lock (_lock)
        {
            var now = clock.GetUtcNow();
            if (now < _blockedUntil) return GateDecision.Deny("rate-limited", _blockedUntil);

            DropExpired(now);
            if (_recentCalls.Count >= _polling.FleetCallsPerMinute)
                return GateDecision.Deny("call-budget", _recentCalls.Peek() + Window);

            _recentCalls.Enqueue(now);
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

    private void DropExpired(DateTimeOffset now)
    {
        while (_recentCalls.Count > 0 && now - _recentCalls.Peek() >= Window)
            _recentCalls.Dequeue();
    }
}

public sealed record GateDecision(bool Allowed, string? Reason, DateTimeOffset? RetryAt)
{
    public static readonly GateDecision Allow = new(true, null, null);

    public static GateDecision Deny(string reason, DateTimeOffset retryAt) => new(false, reason, retryAt);
}
