using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Users;

public sealed class DeviceKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    UserRepository users,
    TimeProvider clock) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DeviceKey";
    public const string UserIdClaim = "uid";
    public const string DeviceIdClaim = "did";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (BearerKey() is not { } key) return AuthenticateResult.NoResult();

        var device = await users.FindDeviceByKeyHashAsync(Secrets.Hash(key), Context.RequestAborted);
        if (device is null) return AuthenticateResult.Fail("Unknown device key");

        users.TouchInBackground(device, clock.GetUtcNow());
        var identity = new ClaimsIdentity(
            [new Claim(UserIdClaim, device.UserId.ToString()), new Claim(DeviceIdClaim, device.Id.ToString())],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    private string? BearerKey()
    {
        var header = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && header.Length > prefix.Length
            ? header[prefix.Length..].Trim()
            : null;
    }
}

public static class CurrentUser
{
    public static long UserId(this ClaimsPrincipal user) => long.Parse(user.FindFirstValue(DeviceKeyAuthenticationHandler.UserIdClaim)!);

    public static long DeviceId(this ClaimsPrincipal user) => long.Parse(user.FindFirstValue(DeviceKeyAuthenticationHandler.DeviceIdClaim)!);
}
