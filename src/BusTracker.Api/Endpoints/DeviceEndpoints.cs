using System.Security.Claims;
using BusTracker.Api.Configuration;
using BusTracker.Api.Users;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Endpoints;

public static class DeviceEndpoints
{
    public const string RegistrationRateLimit = "registration";

    private const int MaxNameLength = 60;

    private static readonly HashSet<string> Platforms = ["web", "android", "ios", "other"];

    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/devices", Register).AllowAnonymous().RequireRateLimiting(RegistrationRateLimit);

        var me = app.MapGroup("/api/me");
        me.MapGet("/", GetMe);
        me.MapPost("/link-codes", CreateLinkCode);
        me.MapDelete("/devices/{id:long}", DeleteDevice);
        return app;
    }

    private static async Task<IResult> Register(
        RegisterDeviceRequest request, UserRepository users, IOptions<BusTrackerOptions> options, TimeProvider clock, CancellationToken ct)
    {
        if (ParsePlatform(request.Platform) is not { } platform)
            return Results.Problem($"Platform must be one of: {string.Join(", ", Platforms)}.", statusCode: StatusCodes.Status400BadRequest);

        var name = DeviceName(request.Name, platform);
        var key = Secrets.NewDeviceKey();
        var now = clock.GetUtcNow();

        if (!string.IsNullOrWhiteSpace(request.LinkCode))
            return await LinkAsync(request.LinkCode, key, platform, name, users, now, ct);

        var access = options.Value.Access;
        if (!HasValidInvite(access, request.Invite))
            return Results.Problem("A valid invite code is required to start using this server.", statusCode: StatusCodes.Status403Forbidden);

        var device = await users.CreateUserWithDeviceAsync(Secrets.Hash(key), platform, name,
            access.RequiresInvite ? request.Invite!.Trim() : null, now, ct);
        return Results.Created("/api/me", new RegisterDeviceResponse(device.Id, key, platform, name, LinkedToExistingUser: false));
    }

    private static string? ParsePlatform(string? platform)
    {
        var normalized = (platform ?? "").Trim().ToLowerInvariant();
        return Platforms.Contains(normalized) ? normalized : null;
    }

    private static string DeviceName(string? name, string platform) =>
        string.IsNullOrWhiteSpace(name) ? $"{platform} device" : name.Trim()[..Math.Min(MaxNameLength, name.Trim().Length)];

    private static bool HasValidInvite(AccessOptions access, string? invite) =>
        !access.RequiresInvite || Secrets.MatchesAny(invite ?? "", access.InviteCodes);

    private static async Task<IResult> LinkAsync(
        string linkCode, string key, string platform, string name, UserRepository users, DateTimeOffset now, CancellationToken ct)
    {
        var normalized = Secrets.NormalizeLinkCode(linkCode);
        var device = normalized.Length == 0
            ? null
            : await users.AddDeviceWithLinkCodeAsync(Secrets.Hash(normalized), Secrets.Hash(key), platform, name, now, ct);
        return device is null
            ? Results.Problem("The link code is invalid or has expired.", statusCode: StatusCodes.Status400BadRequest)
            : Results.Created("/api/me", new RegisterDeviceResponse(device.Id, key, platform, name, LinkedToExistingUser: true));
    }

    private static async Task<MeResponse> GetMe(ClaimsPrincipal user, UserRepository users, CancellationToken ct)
    {
        var current = user.DeviceId();
        var devices = await users.GetDevicesAsync(user.UserId(), ct);
        return new MeResponse(current,
            devices.Select(d => new DeviceView(d.Id, d.Platform, d.Name, d.CreatedAt, d.LastSeenAt, d.Id == current)).ToList());
    }

    private static async Task<LinkCodeResponse> CreateLinkCode(
        ClaimsPrincipal user, UserRepository users, IOptions<BusTrackerOptions> options, TimeProvider clock, CancellationToken ct)
    {
        var code = Secrets.NewLinkCode();
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(options.Value.Access.LinkCodeMinutes);
        await users.CreateLinkCodeAsync(user.UserId(), Secrets.Hash(code), now, expiresAt, ct);
        return new LinkCodeResponse(code, expiresAt);
    }

    private static async Task<IResult> DeleteDevice(long id, ClaimsPrincipal user, UserRepository users, CancellationToken ct) =>
        await users.DeleteDeviceAsync(user.UserId(), id, ct) ? Results.NoContent() : Results.NotFound();
}

public sealed record RegisterDeviceRequest(string? Platform, string? Name, string? Invite, string? LinkCode);

public sealed record RegisterDeviceResponse(long DeviceId, string Key, string Platform, string Name, bool LinkedToExistingUser);

public sealed record DeviceView(long Id, string Platform, string Name, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool Current);

public sealed record MeResponse(long CurrentDeviceId, IReadOnlyList<DeviceView> Devices);

public sealed record LinkCodeResponse(string Code, DateTimeOffset ExpiresAt);
