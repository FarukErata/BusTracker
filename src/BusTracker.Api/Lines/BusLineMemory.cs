using System.Collections.Concurrent;
using BusTracker.Api.Configuration;
using BusTracker.Api.Data;
using BusTracker.Api.Ibb;
using Dapper;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Lines;

public sealed record BusAssignment(
    string DoorNo, string LineCode, string RouteCode, string Destination, DateTimeOffset ConfirmedAt, LiveBus? Reported);

public sealed class BusLineMemory(Database database, IOptions<BusTrackerOptions> options, TimeProvider clock, ILogger<BusLineMemory> logger)
{
    private const string UpsertSql =
        """
        INSERT INTO bus_lines (door_no, line_code, route_code, destination, confirmed_at)
        VALUES (@DoorNo, @LineCode, @RouteCode, @Destination, @ConfirmedAt)
        ON CONFLICT (door_no) DO UPDATE SET
            line_code = excluded.line_code, route_code = excluded.route_code,
            destination = excluded.destination, confirmed_at = excluded.confirmed_at;
        """;

    private readonly TimeSpan _trust = TimeSpan.FromHours(options.Value.Polling.MemoryTrustHours);
    private readonly ConcurrentDictionary<string, BusAssignment> _byDoor = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _rosterAt = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _loaded;
    private long _version;

    public long Version => Interlocked.Read(ref _version);

    public int Count => _byDoor.Count;

    public bool KnowsOn(string doorNo, string lineCode) =>
        _byDoor.TryGetValue(doorNo, out var assignment)
        && assignment.LineCode == lineCode
        && clock.GetUtcNow() - assignment.ConfirmedAt < _trust;

    public DateTimeOffset? RosterAt(string lineCode) => _rosterAt.TryGetValue(lineCode, out var at) ? at : null;

    public async Task<IReadOnlyList<BusAssignment>> OnLineAsync(string lineCode, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        var now = clock.GetUtcNow();
        return _byDoor.Values.Where(a => a.LineCode == lineCode && now - a.ConfirmedAt < _trust).ToList();
    }

    public async Task ApplyRosterAsync(string lineCode, IReadOnlyList<LiveBus> buses, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        await _lock.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            var present = buses.GroupBy(b => b.DoorNo).Select(g => g.MaxBy(b => b.ReportedAt)!).ToList();
            var doors = present.Select(b => b.DoorNo).ToHashSet();
            var left = _byDoor.Values.Where(a => a.LineCode == lineCode && !doors.Contains(a.DoorNo)).Select(a => a.DoorNo).ToList();

            await using var connection = await database.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await connection.ExecuteAsync(UpsertSql, present.Select(b => new
            {
                b.DoorNo, LineCode = lineCode, b.RouteCode, b.Destination, ConfirmedAt = DbTime.Format(now),
            }), transaction);
            await connection.ExecuteAsync(
                "DELETE FROM bus_lines WHERE door_no = @DoorNo AND line_code = @LineCode;",
                left.Select(d => new { DoorNo = d, LineCode = lineCode }), transaction);
            await transaction.CommitAsync(ct);

            foreach (var bus in present)
                _byDoor[bus.DoorNo] = new BusAssignment(bus.DoorNo, lineCode, bus.RouteCode, bus.Destination, now, bus);
            foreach (var door in left)
                _byDoor.TryRemove(door, out _);
            _rosterAt[lineCode] = now;
            Interlocked.Increment(ref _version);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Reassign(BusAssignment assignment, string routeCode, string destination)
    {
        if (_byDoor.TryGetValue(assignment.DoorNo, out var current) && current.LineCode == assignment.LineCode)
            _byDoor[assignment.DoorNo] = current with { RouteCode = routeCode, Destination = destination };
        Interlocked.Increment(ref _version);
        using var detached = ExecutionContext.SuppressFlow();
        _ = Task.Run(() => PersistReassignAsync(assignment.DoorNo, assignment.LineCode, routeCode, destination));
    }

    private async Task PersistReassignAsync(string doorNo, string lineCode, string routeCode, string destination)
    {
        await _lock.WaitAsync();
        try
        {
            await using var connection = await database.OpenAsync(CancellationToken.None);
            await connection.ExecuteAsync(
                "UPDATE bus_lines SET route_code = @routeCode, destination = @destination WHERE door_no = @doorNo AND line_code = @lineCode;",
                new { routeCode, destination, doorNo, lineCode });
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not save the new direction of bus {Door}", doorNo);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded) return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_loaded) return;

            await using var connection = await database.OpenAsync(ct);
            var rows = await connection.QueryAsync<Row>(
                "SELECT door_no AS DoorNo, line_code AS LineCode, route_code AS RouteCode, destination AS Destination, confirmed_at AS ConfirmedAt FROM bus_lines;");
            foreach (var row in rows)
                _byDoor[row.DoorNo] = new BusAssignment(
                    row.DoorNo, row.LineCode, row.RouteCode, row.Destination, DbTime.Parse(row.ConfirmedAt)!.Value, null);
            foreach (var line in _byDoor.Values.GroupBy(a => a.LineCode))
                _rosterAt.TryAdd(line.Key, line.Max(a => a.ConfirmedAt));
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record Row(string DoorNo, string LineCode, string RouteCode, string Destination, string ConfirmedAt);
}
