using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using BusTracker.Api.Configuration;
using BusTracker.Api.Ibb;
using BusTracker.Api.Shapes;
using BusTracker.Api.Stops;
using BusTracker.Api.Tracing;
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
    HopTimes hops,
    EtaAccuracy accuracy,
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
    private readonly ConcurrentDictionary<string, Lazy<Task<GateDecision?>>> _rosterRefreshes = new();
    private readonly ConcurrentDictionary<string, GateDecision> _rosterDenied = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DateTimeOffset>> _newcomers = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _newcomerRechecks = new();
    private readonly TrackerEvents _events = new(hops.Record, accuracy.Crossed);
    private static readonly int[] SampledStopsAhead = [0, 4, 9, 14];

    private TimeSpan StaticMaxAge => TimeSpan.FromHours(_options.StaticData.RefreshHours);
    private TimeSpan RosterMaxAge => TimeSpan.FromMinutes(_options.Polling.RosterMaxAgeMinutes);
    private TimeSpan RosterRecheck => TimeSpan.FromMinutes(_options.Polling.RosterRecheckMinutes);
    private TimeSpan StaleAfter => TimeSpan.FromSeconds(_options.Tracking.StaleAfterSeconds);
    private static readonly TimeSpan NewcomerMemory = TimeSpan.FromHours(6);
    private static readonly TimeSpan NewcomerCooldown = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan RetryAfterError = TimeSpan.FromMinutes(1);
    private const double DepartureWindowMeters = 1500;
    private const double OnRouteMeters = 50;

    public async Task<IReadOnlyList<RouteStop>> GetStopsAsync(string lineCode, CancellationToken ct)
    {
        if (TryGetFresh(_stops, lineCode) is { } cached) return cached;

        var indexed = await stopIndex.GetRouteStopsAsync(lineCode, ct);
        if (indexed.Count == 0 && (await stopIndex.GetStateAsync(ct)).Count > 0) return [];
        var stops = indexed.Count > 0 ? indexed : await ibb.GetRouteStopsAsync(lineCode, ct);
        if (stops.Count > 0) _stops[lineCode] = new Cached<IReadOnlyList<RouteStop>>(stops, clock.GetUtcNow());
        return stops;
    }

    public async Task<LineTracks> GetTracksAsync(string lineCode, CancellationToken ct)
    {
        if (TryGetFresh(_tracks, lineCode) is { } cached) return cached;

        var stops = await GetStopsAsync(lineCode, ct);
        var lineShapes = await shapes.GetForLineAsync(lineCode, ct);
        var tracks = stops.Count == 0 ? LineTracks.Empty : LineTracks.Build(stops, lineShapes, _options.Tracking.StopOnRouteMeters);
        if (stops.Count > 0 && lineShapes.Count > 0) _tracks[lineCode] = new Cached<LineTracks>(tracks, clock.GetUtcNow());
        return tracks;
    }

    public async Task<LiveResult> GetLiveAsync(string lineCode, CancellationToken ct, bool refreshRoster = true)
    {
        await memory.EnsureLoadedAsync(ct);
        var tracks = await GetTracksAsync(lineCode, ct);
        var denied = refreshRoster && IsRosterDue(lineCode) ? await RefreshRosterAsync(lineCode, ct) : null;
        var lineLock = _lineLocks.GetOrAdd(lineCode, _ => new SemaphoreSlim(1, 1));
        await Telemetry.EnterAsync(lineLock, $"line {lineCode}", ct);
        try
        {
            var assignments = await memory.OnLineAsync(lineCode, ct);
            if (assignments.Count == 0 && memory.RosterAt(lineCode) is null)
                throw new LineUnavailableException(denied?.Reason ?? "not-loaded", denied?.RetryAt);

            var fleet = await fleetFeed.GetAsync(ct);
            return WithFreshness(Track(lineCode, assignments, fleet, tracks));
        }
        finally
        {
            lineLock.Release();
        }
    }

    public DateTimeOffset? LastFetchedAt(string lineCode) => memory.RosterAt(lineCode);

    public HopTimes Hops => hops;

    public EtaContext Eta(DateTimeOffset now) => new(hops, now, _options.Tracking.UseHopTimes);

    public Task WhenIdleAsync() =>
        Task.WhenAll(_rosterRefreshes.Values.Select(r => r.Value).Append(fleetFeed.WhenIdleAsync()))
            .ContinueWith(_ => { }, TaskScheduler.Default);

    private T? TryGetFresh<T>(ConcurrentDictionary<string, Cached<T>> cache, string lineCode) where T : class =>
        cache.TryGetValue(lineCode, out var cached) && clock.GetUtcNow() - cached.At < StaticMaxAge ? cached.Value : null;

    private async Task<GateDecision?> RefreshRosterAsync(string lineCode, CancellationToken ct)
    {
        var known = memory.RosterAt(lineCode) is not null || (await memory.OnLineAsync(lineCode, ct)).Count > 0;
        if (StillDenied(lineCode) is { } denied) return known ? null : denied;
        var refresh = _rosterRefreshes.GetOrAdd(lineCode, code => new Lazy<Task<GateDecision?>>(() => Task.Run(() => FetchRosterAsync(code)))).Value;
        if (!known)
        {
            using var wait = Telemetry.Span("wait", $"roster {lineCode}");
            return await refresh;
        }

        return null;
    }

    private async Task<GateDecision?> FetchRosterAsync(string lineCode)
    {
        using var trace = Telemetry.Background("roster refresh", lineCode);
        try
        {
            var decision = await FetchRosterCoreAsync(lineCode);
            if (decision is { Allowed: false }) _rosterDenied[lineCode] = decision;
            else _rosterDenied.TryRemove(lineCode, out _);
            return decision;
        }
        finally
        {
            _rosterRefreshes.TryRemove(lineCode, out _);
        }
    }

    private async Task<GateDecision?> FetchRosterCoreAsync(string lineCode)
    {
        var decision = gate.TryAcquire(FleetCallKind.Roster);
        if (!decision.Allowed)
        {
            FleetFeed.Denied($"roster {lineCode}", decision);
            return decision;
        }

        try
        {
            var buses = await ibb.GetLiveBusesAsync(lineCode, CancellationToken.None);
            gate.ReportSuccess();
            var lineLock = _lineLocks.GetOrAdd(lineCode, _ => new SemaphoreSlim(1, 1));
            await Telemetry.EnterAsync(lineLock, $"line {lineCode}", CancellationToken.None);
            try
            {
                await memory.ApplyRosterAsync(lineCode, buses, CancellationToken.None);
                _checks.TryRemove(lineCode, out _);
            }
            finally
            {
                lineLock.Release();
            }
            return null;
        }
        catch (IbbRateLimitedException)
        {
            gate.ReportRateLimited();
            logger.LogWarning("İBB rate limit hit while fetching {Line}; backing off until {Until}", lineCode, gate.BlockedUntil);
            return GateDecision.Deny("rate-limited", gate.BlockedUntil ?? clock.GetUtcNow());
        }
        catch (Exception e) when (e is IbbException or HttpRequestException or OperationCanceledException
                                      or System.Text.Json.JsonException or System.Data.Common.DbException)
        {
            logger.LogWarning(e, "Roster refresh for {Line} failed", lineCode);
            return GateDecision.Deny("ibb-error", clock.GetUtcNow() + RetryAfterError);
        }
    }

    private GateDecision? StillDenied(string lineCode) =>
        _rosterDenied.TryGetValue(lineCode, out var denied) && denied.RetryAt > clock.GetUtcNow() ? denied : null;

    private bool IsRosterDue(string lineCode)
    {
        if (memory.RosterAt(lineCode) is not { } at) return true;

        var age = clock.GetUtcNow() - at;
        return age >= RosterMaxAge || _checks.TryGetValue(lineCode, out var check) && check.Recheck && age >= RosterRecheck;
    }

    private LiveSnapshot Track(string lineCode, IReadOnlyList<BusAssignment> assignments, FleetSnapshot? fleet, LineTracks tracks)
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
        var tracked = tracker.Update(buses, tracks, now, _events).ToList();

        var turns = Turnarounds.Find(previous?.Snapshot.Buses ?? [], tracked, tracks, _options.Tracking);
        tracked = ApplyTurnarounds(tracked, turns, assignments, tracker, tracks, now);

        FlagForRecheck(lineCode, check, turns);
        FlagNewcomers(lineCode, fleet, tracks, now);
        SamplePredictions(tracked, tracks, now);

        var snapshot = new LiveSnapshot(tracked, PositionsAt(lineCode, fleet, buses, now));
        _snapshots[lineCode] = new LineSnapshot(snapshot, memory.Version, fleet?.FetchedAt);
        return snapshot;
    }

    private bool IsCurrent([NotNullWhen(true)] LineSnapshot? previous, FleetSnapshot? fleet) =>
        previous is not null && previous.MemoryVersion == memory.Version && previous.FleetAt == fleet?.FetchedAt;

    private void FlagForRecheck(string lineCode, LineCheck check, TurnaroundResult turns)
    {
        if (turns.Flipped.Count > 0 || turns.Lost.Count > 0)
            _checks[lineCode] = new LineCheck(Recheck: true, Lost: new HashSet<string>(check.Lost.Concat(turns.Lost)));
    }

    private void SamplePredictions(IReadOnlyList<TrackedBus> tracked, LineTracks tracks, DateTimeOffset now)
    {
        foreach (var bus in tracked)
        {
            if (bus is not { Status: BusStatus.Ok, AlongMeters: { } along }) continue;
            if (tracks.For(bus.Bus.RouteCode, bus.Bus.Direction) is not { } track || track.NextStop(along) is not { } next) continue;

            foreach (var ahead in SampledStopsAhead)
            {
                if (next.Index + ahead >= track.Stops.Count) break;
                var stop = track.Stops[next.Index + ahead];
                var eta = Arrivals.EstimateBoth(bus, track, stop, hops, now);
                accuracy.Predicted(new EtaPrediction(track.RouteCode, bus.Bus.DoorNo, stop.Index, now,
                    eta.SpeedSeconds, eta.HopSeconds, stop.AlongMeters - along, bus.AgeSeconds));
            }
        }
    }

    private void FlagNewcomers(string lineCode, FleetSnapshot? fleet, LineTracks tracks, DateTimeOffset now)
    {
        if (fleet is null || tracks.Mains.Count == 0) return;
        if (_newcomerRechecks.TryGetValue(lineCode, out var last) && now - last < NewcomerCooldown) return;

        var seen = _newcomers.GetOrAdd(lineCode, _ => new ConcurrentDictionary<string, DateTimeOffset>());
        foreach (var (door, at) in seen)
            if (now - at > NewcomerMemory) seen.TryRemove(door, out _);

        var arrived = fleet.Vehicles.Values.Where(v => IsNewcomer(v, lineCode, tracks, now)).Count(v => seen.TryAdd(v.DoorNo, now));
        if (arrived == 0) return;

        _newcomerRechecks[lineCode] = now;
        _checks.AddOrUpdate(lineCode, _ => LineCheck.None with { Recheck = true }, (_, current) => current with { Recheck = true });
        logger.LogInformation("{Count} unknown buses at the start of {Line}; rechecking its roster", arrived, lineCode);
    }

    private bool IsNewcomer(FleetVehicle vehicle, string lineCode, LineTracks tracks, DateTimeOffset now) =>
        now - vehicle.ReportedAt < StaleAfter
        && !memory.KnowsOn(vehicle.DoorNo, lineCode)
        && tracks.Mains.Any(track => JustLeftTheStart(track, vehicle));

    private bool JustLeftTheStart(RouteTrack track, FleetVehicle vehicle)
    {
        if (track.Stops.Count == 0) return false;
        var start = track.Stops[0];
        if (Geo.DistanceMeters(start.Lat, start.Lng, vehicle.Lat, vehicle.Lng) > DepartureWindowMeters + _options.Tracking.TerminalMeters) return false;

        var from = start.AlongMeters + _options.Tracking.TerminalMeters;
        var projection = track.Geometry.Project(new LatLng(vehicle.Lat, vehicle.Lng), from, start.AlongMeters + DepartureWindowMeters);
        return projection.OffsetMeters <= OnRouteMeters && projection.AlongMeters >= from;
    }

    private List<TrackedBus> ApplyTurnarounds(
        List<TrackedBus> tracked, TurnaroundResult turns, IReadOnlyList<BusAssignment> assignments,
        LineTracker tracker, LineTracks tracks, DateTimeOffset now)
    {
        if (turns.Flipped.Count == 0 && turns.Lost.Count == 0) return tracked;

        foreach (var bus in turns.Flipped)
        {
            tracker.Reset(bus.DoorNo);
            if (assignments.FirstOrDefault(a => a.DoorNo == bus.DoorNo) is { } assignment)
                memory.Reassign(assignment, bus.RouteCode, bus.Destination);
            logger.LogInformation("Bus {Door} turned around on {Line}: now {Route}", bus.DoorNo, bus.LineCode, bus.RouteCode);
        }

        var flippedDoors = turns.Flipped.Select(b => b.DoorNo).ToHashSet();
        return tracked
            .Where(b => !flippedDoors.Contains(b.Bus.DoorNo) && !turns.Lost.Contains(b.Bus.DoorNo))
            .Concat(tracker.Update(turns.Flipped, tracks, now, _events))
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
