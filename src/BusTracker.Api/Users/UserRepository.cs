using BusTracker.Api.Data;
using Dapper;

namespace BusTracker.Api.Users;

public sealed record Device(long Id, long UserId, string Platform, string Name, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);

public enum DeviceRemoval { Removed, NotFound, LastDevice }

public sealed class UserRepository(Database database, ILogger<UserRepository> logger)
{
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    public async Task<Device> CreateUserWithDeviceAsync(
        string keyHash, string platform, string name, string? inviteCode, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var userId = await connection.ExecuteScalarAsync<long>(
            "INSERT INTO users (created_at, invite_code) VALUES (@CreatedAt, @inviteCode) RETURNING id;",
            new { CreatedAt = DbTime.Format(now), inviteCode }, transaction);
        var device = await InsertDeviceAsync(connection, transaction, userId, keyHash, platform, name, now);
        await transaction.CommitAsync(ct);
        return device;
    }

    public async Task<Device?> AddDeviceWithLinkCodeAsync(
        string codeHash, string keyHash, string platform, string name, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var userId = await connection.ExecuteScalarAsync<long?>(
            "DELETE FROM link_codes WHERE code_hash = @codeHash AND expires_at > @Now RETURNING user_id;",
            new { codeHash, Now = DbTime.Format(now) }, transaction);
        if (userId is null) return null;

        var device = await InsertDeviceAsync(connection, transaction, userId.Value, keyHash, platform, name, now);
        await transaction.CommitAsync(ct);
        return device;
    }

    public async Task<Device?> FindDeviceByKeyHashAsync(string keyHash, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<DeviceRow>(
            $"{SelectDevice} WHERE key_hash = @keyHash;", new { keyHash });
        return row?.ToDevice();
    }

    public void TouchInBackground(Device device, DateTimeOffset now)
    {
        if (now - device.LastSeenAt < TouchInterval) return;

        using var detached = ExecutionContext.SuppressFlow();
        _ = Task.Run(async () =>
        {
            try
            {
                await using var connection = await database.OpenAsync(CancellationToken.None);
                await connection.ExecuteAsync("UPDATE devices SET last_seen_at = @Now WHERE id = @Id;",
                    new { Now = DbTime.Format(now), device.Id });
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Could not update last seen for device {Device}", device.Id);
            }
        });
    }

    public async Task<IReadOnlyList<Device>> GetDevicesAsync(long userId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        var rows = await connection.QueryAsync<DeviceRow>($"{SelectDevice} WHERE user_id = @userId ORDER BY id;", new { userId });
        return rows.Select(r => r.ToDevice()).ToList();
    }

    public async Task<DeviceRemoval> DeleteDeviceAsync(long userId, long deviceId, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        var deleted = await connection.ExecuteAsync(
            """
            DELETE FROM devices
            WHERE id = @deviceId AND user_id = @userId AND (SELECT COUNT(*) FROM devices WHERE user_id = @userId) > 1;
            """,
            new { deviceId, userId });
        if (deleted > 0) return DeviceRemoval.Removed;

        var exists = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM devices WHERE id = @deviceId AND user_id = @userId;", new { deviceId, userId });
        return exists > 0 ? DeviceRemoval.LastDevice : DeviceRemoval.NotFound;
    }

    public async Task CreateLinkCodeAsync(long userId, string codeHash, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken ct)
    {
        await using var connection = await database.OpenAsync(ct);
        await connection.ExecuteAsync(
            """
            DELETE FROM link_codes WHERE expires_at <= @Now OR user_id = @userId;
            INSERT INTO link_codes (code_hash, user_id, expires_at) VALUES (@codeHash, @userId, @ExpiresAt);
            """,
            new { codeHash, userId, Now = DbTime.Format(now), ExpiresAt = DbTime.Format(expiresAt) });
    }

    private const string SelectDevice =
        "SELECT id AS Id, user_id AS UserId, platform AS Platform, name AS Name, created_at AS CreatedAt, last_seen_at AS LastSeenAt FROM devices";

    private static async Task<Device> InsertDeviceAsync(
        System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction,
        long userId, string keyHash, string platform, string name, DateTimeOffset now)
    {
        var id = await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO devices (user_id, key_hash, platform, name, created_at, last_seen_at)
            VALUES (@userId, @keyHash, @platform, @name, @Now, @Now) RETURNING id;
            """,
            new { userId, keyHash, platform, name, Now = DbTime.Format(now) }, transaction);
        return new Device(id, userId, platform, name, now, now);
    }

    private sealed record DeviceRow(long Id, long UserId, string Platform, string Name, string CreatedAt, string LastSeenAt)
    {
        public Device ToDevice() => new(Id, UserId, Platform, Name, DbTime.Parse(CreatedAt)!.Value, DbTime.Parse(LastSeenAt)!.Value);
    }
}
