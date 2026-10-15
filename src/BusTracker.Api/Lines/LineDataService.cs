using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using BusTracker.Api.Configuration;
using BusTracker.Api.Ibb;
using BusTracker.Api.Shapes;
using BusTracker.Api.Stops;
using BusTracker.Api.Tracking;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Lines;

public sealed record LiveSnapshot(IReadOnlyList<TrackedBus> Buses, DateTimeOffset FetchedAt);

public sealed record LiveResult(LiveSnapshot Snapshot, bool Stale, string? StaleReason, DateTimeOffset? RetryAt);

public sealed class LineUnavailableException(string reason, DateTimeOffset? retryAt)
    : Exception($"Live data unavailable: {reason}")
{
    public string Reason { get; } = reason;
    public DateTimeOffset? RetryAt { get; } = retryAt;
}

public sealed class LineDataService(
    IbbClient ibb,
    FleetCallGate gate,
    FleetFeed fleetFeed,
    BusLineMemory memory,
    RouteShapeRepository shapes,
    StopIndexRepository stopIndex,
    IOptions<BusTrackerOptions> options,
    TimeProvider clock,
    ILogger<LineDataService> logger)
{
    private readonly BusTrackerOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, Cached<IReadOnlyList<RouteStop>>> _stops = new();
    private readonly ConcurrentDictionary<string, Cached<LineTracks>> _tracks = new();
    private readonly ConcurrentDictionary<string, LineCheck> _checks = new();
    private readonly ConcurrentDictionary<string, LineSnapshot> _snapshots = new();
    private readonly ConcurrentDictionary<string, LineTracker> _trackers = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lineLocks = new();

    private TimeSpan StaticMaxAge => TimeSpan.FromHours(_options.StaticData.RefreshHours);
    private TimeSpan RosterMaxAge => TimeSpan.FromMinutes(_options.Polling.RosterMaxAgeMinutes);
    private TimeSpan RosterRecheck => TimeSpan.FromMinutes(_options.Polling.RosterRecheckMinutes);

    public async Task<IReadOnlyList<RouteStop>> GetStopsAsync(string lineCode, CancellationToken ct)
    {
        if (TryGetFresh(_stops, lineCode) is { } cached) return cached;

        var indexed = await stopIndex.GetRouteStopsAsync(lineCode, ct);
        var stops = indexed.Count > 0 ? indexed : await ibb.GetRouteStopsAsync(lineCode, ct);
        _stops[lineCode] = new Cached<IReadOnlyList<RouteStop>>(stops, clock.GetUtcNow());
        return stops;
    }

    public async Task<LineTracks> GetTracksAsync(string lineCode, CancellationToken ct)
    {
        if (TryGetFresh(_tracks, lineCode) is { } cached) return cached;

        var stops = await GetStopsAsync(lineCode, ct);
        var lineShapes = await shapes.GetForLineAsync(lineCode, ct);
        var tracks = stops.Count == 0 ? LineTracks.Empty : LineTracks.Build(stops, lineShapes, _options.Tracking.StopOnRouteMeters);
        _tracks[lineCode] = new Cached<LineTracks>(tracks, clock.GetUtcNow());
        return tracks;
    }

    public async Task<LiveResult> GetLiveAsync(string lineCode, CancellationToken ct, bool refreshRoster = true)
    {
        var tracks = await GetTracksAsync(lineCode, ct);
        var lineLock = _lineLocks.GetOrAdd(lineCode, _ => new SemaphoreSlim(1, 1));
        await lineLock.WaitAsync(ct);
        try
        {
            var denied = refreshRoster ? await RefreshRosterIfDueAsync(lineCode, ct) : null;
            var assignments = await memory.OnLineAsync(lineCode, ct);
            if (assignments.Count == 0 && memory.RosterAt(lineCode) is null)
                throw new LineUnavailableException(denied?.Reason ?? "not-loaded", denied?.RetryAt);

            var fleet = await fleetFeed.GetAsync(ct);
            return WithFreshness(await TrackAsync(lineCode, assignments, fleet, tracks, ct));
        }
        finally
        {
            lineLock.Release();
        }
    }

    public DateTimeOffset? LastFetchedAt(string lineCode) => memory.RosterAt(lineCode);

    private T? TryGetFresh<T>(ConcurrentDictionary<string, Cached<T>> cache, string lineCode) where T : class =>
        cache.TryGetValue(lineCode, out var cached) && clock.GetUtcNow() - cached.At < StaticMaxAge ? cached.Value : null;

    private async Task<GateDecision?> RefreshRosterIfDueAsync(string lineCode, CancellationToken ct)
    {
        if (!IsRosterDue(lineCode)) return null;

        var decision = gate.TryAcquire(FleetCallKind.Roster);
        if (!decision.Allowed) return decision;

        try
        {
            var buses = await ibb.GetLiveBusesAsync(lineCode, ct);
            gate.ReportSuccess();
            await memory.ApplyRosterAsync(lineCode, buses, ct);
            _checks.TryRemove(lineCode, out _);
            return null;
        }
        catch (IbbRateLimitedException)
        {
            gate.ReportRateLimited();
            logger.LogWarning("İBB rate limit hit while fetching {Line}; backing off until {Until}", lineCode, gate.BlockedUntil);
            return GateDecision.Deny("rate-limited", gate.BlockedUntil ?? clock.GetUtcNow());
        }
    }

    private bool IsRosterDue(string lineCode)
    {
        if (memory.RosterAt(lineCode) is not { } at) return true;

        var age = clock.GetUtcNow() - at;
        return age >= RosterMaxAge || _checks.TryGetValue(lineCode, out var check) && check.Recheck && age >= RosterRecheck;
    }

    private async Task<LiveSnapshot> TrackAsync(
        string lineCode, IReadOnlyList<BusAssignment> assignments, FleetSnapshot? fleet, LineTracks tracks, CancellationToken ct)
    {
        _snapshots.TryGetValue(lineCode, out var previous);
        if (IsCurrent(previous, fleet)) return previous.Snapshot;

        var check = _checks.GetValueOrDefault(lineCode) ?? LineCheck.None;
        var buses = assignments
            .Where(a => !check.Lost.Contains(a.DoorNo))
            .Select(a => ToLiveBus(a, fleet))
            .OfType<LiveBus>()
            .ToList();

        var now = clock.GetUtcNow();
        var tracker = _trackers.GetOrAdd(lineCode, _ => new LineTracker(_options.Tracking));
        var tracked = tracker.Update(buses, tracks, now).ToList();

        var turns = Turnarounds.Find(previous?.Snapshot.Buses ?? [], tracked, tracks, _options.Tracking);
        tracked = await ApplyTurnaroundsAsync(tracked, turns, assignments, tracker, tracks, now, ct);

        FlagForRecheck(lineCode, check, turns, tracked);

        var snapshot = new LiveSnapshot(tracked, PositionsAt(lineCode, fleet, buses, now));
        _snapshots[lineCode] = new LineSnapshot(snapshot, memory.Version, fleet?.FetchedAt);
        return snapshot;
    }

    private bool IsCurrent([NotNullWhen(true)] LineSnapshot? previous, FleetSnapshot? fleet) =>
        previous is not null && previous.MemoryVersion == memory.Version && previous.FleetAt == fleet?.FetchedAt;

    private void FlagForRecheck(string lineCode, LineCheck check, TurnaroundResult turns, IReadOnlyList<TrackedBus> tracked)
    {
        var suspicious = turns.Flipped.Count > 0 || turns.Lost.Count > 0 || tracked.Any(b => b.Status == BusStatus.OffRoute);
        if (suspicious)
            _checks[lineCode] = new LineCheck(Recheck: true, Lost: new HashSet<string>(check.Lost.Concat(turns.Lost)));
    }

    private async Task<List<TrackedBus>> ApplyTurnaroundsAsync(
        List<TrackedBus> tracked, TurnaroundResult turns, IReadOnlyList<BusAssignment> assignments,
        LineTracker tracker, LineTracks tracks, DateTimeOffset now, CancellationToken ct)
    {
        if (turns.Flipped.Count == 0 && turns.Lost.Count == 0) return tracked;

        foreach (var bus in turns.Flipped)
        {
            tracker.Reset(bus.DoorNo);
            if (assignments.FirstOrDefault(a => a.DoorNo == bus.DoorNo) is { } assignment)
                await memory.ReassignAsync(assignment, bus.RouteCode, bus.Destination, ct);
            logger.LogInformation("Bus {Door} turned around on {Line}: now {Route}", bus.DoorNo, bus.LineCode, bus.RouteCode);
        }

        var flippedDoors = turns.Flipped.Select(b => b.DoorNo).ToHashSet();
        return tracked
            .Where(b => !flippedDoors.Contains(b.Bus.DoorNo) && !turns.Lost.Contains(b.Bus.DoorNo))
            .Concat(tracker.Update(turns.Flipped, tracks, now))
            .ToList();
    }

    private static LiveBus? ToLiveBus(BusAssignment assignment, FleetSnapshot? fleet)
    {
        if (LatestPosition(assignment.Reported, fleet?.Vehicles.GetValueOrDefault(assignment.DoorNo)) is not { } position)
            return null;

        var (direction, variant) = IbbParsers.SplitRouteCode(assignment.RouteCode);
        var nearestStop = assignment.Reported?.RouteCode == assignment.RouteCode ? assignment.Reported.NearestStopCode : "";
        return new LiveBus(
            assignment.DoorNo, position.Lat, position.Lng, assignment.LineCode, assignment.RouteCode,
            direction, variant, assignment.Destination, position.ReportedAt, nearestStop);
    }

    private static FleetVehicle? LatestPosition(LiveBus? reported, FleetVehicle? vehicle)
    {
        var fromRoster = reported is null ? null : new FleetVehicle(reported.DoorNo, reported.Lat, reported.Lng, reported.ReportedAt);
        if (vehicle is null) return fromRoster;
        return fromRoster is null || vehicle.ReportedAt > fromRoster.ReportedAt ? vehicle : fromRoster;
    }

    private DateTimeOffset PositionsAt(string lineCode, FleetSnapshot? fleet, IReadOnlyList<LiveBus> buses, DateTimeOffset now)
    {
        DateTimeOffset?[] candidates = [fleet?.FetchedAt, memory.RosterAt(lineCode)];
        return candidates.Max() ?? (buses.Count > 0 ? buses.Max(b => b.ReportedAt) : now);
    }

    private LiveResult WithFreshness(LiveSnapshot snapshot)
    {
        if (clock.GetUtcNow() - snapshot.FetchedAt < fleetFeed.Interval * 2) return new LiveResult(snapshot, false, null, null);

        var blockedUntil = gate.BlockedUntil;
        return new LiveResult(snapshot, true, blockedUntil is null ? "positions-delayed" : "rate-limited", blockedUntil);
    }

    private sealed record Cached<T>(T Value, DateTimeOffset At);

    private sealed record LineCheck(bool Recheck, IReadOnlySet<string> Lost)
    {
        public static readonly LineCheck None = new(false, new HashSet<string>());
    }

    private sealed record LineSnapshot(LiveSnapshot Snapshot, long MemoryVersion, DateTimeOffset? FleetAt);
}
