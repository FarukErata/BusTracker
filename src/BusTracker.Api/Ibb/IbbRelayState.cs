using BusTracker.Api.Configuration;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Ibb;

/// <summary>
/// Observes the transport health of İBB calls (fleet, roster, timetable, metro) so <c>/health</c>
/// can show whether the Turkey relay is up, separately from the İBB rate-limit backoff. A relay or
/// tunnel failure shows up here as a transport error; an İBB rate-limit does not (it is a normal HTTP
/// 200 with a SOAP fault, so the transport still counts as a success). See docs/relayed-connection.md.
/// </summary>
public sealed class IbbRelayState(IOptions<BusTrackerOptions> options, TimeProvider clock)
{
    private readonly IbbOptions _ibb = options.Value.Ibb;
    private readonly Lock _lock = new();
    private DateTimeOffset? _lastOkAt;
    private DateTimeOffset? _lastErrorAt;
    private string? _lastError;

    /// <summary>Whether İBB traffic is routed through a proxy (the relay) rather than going direct.</summary>
    public bool Proxied => _ibb.UsesProxy;

    /// <summary>The relay host:port, for display. Null when direct. Never includes credentials.</summary>
    public string? ProxyDisplay => Proxied ? Redact(_ibb.Proxy!) : null;

    /// <summary>A transport-level success: the call reached İBB and came back (rate-limited or not).</summary>
    public void RecordSuccess()
    {
        lock (_lock) _lastOkAt = clock.GetUtcNow();
    }

    /// <summary>A transport-level failure: the relay/tunnel or network could not be reached.</summary>
    public void RecordError(string reason)
    {
        lock (_lock)
        {
            _lastErrorAt = clock.GetUtcNow();
            _lastError = reason;
        }
    }

    public RelayHealth Snapshot()
    {
        lock (_lock) return new RelayHealth(Proxied, ProxyDisplay, _lastOkAt, _lastErrorAt, _lastError);
    }

    // uri.Authority is host[:port] with no scheme and no user:password, so any credentials are dropped.
    private static string Redact(string proxy) =>
        Uri.TryCreate(proxy, UriKind.Absolute, out var uri) ? $"{uri.Scheme}://{uri.Authority}" : proxy;
}

public sealed record RelayHealth(
    bool Proxied, string? Proxy, DateTimeOffset? LastOkAt, DateTimeOffset? LastErrorAt, string? LastError);
