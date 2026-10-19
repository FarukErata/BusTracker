using System.Net;
using System.Net.Http.Json;
using BusTracker.Api.Endpoints;

namespace BusTracker.Tests.Endpoints;

public sealed class DeviceEndpointTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    private async Task<RegisterDeviceResponse> RegisterAsync(string platform = "web", string? linkCode = null)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/devices", new RegisterDeviceRequest(platform, null, null, linkCode));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RegisterDeviceResponse>())!;
    }

    [Fact]
    public async Task Register_ReturnsAKeyThatIdentifiesTheDevice()
    {
        var registered = await RegisterAsync("android");

        var me = await factory.WithKey(registered.Key).GetFromJsonAsync<MeResponse>("/api/me");

        Assert.False(registered.LinkedToExistingUser);
        Assert.True(registered.Key.Length >= 40);
        var device = Assert.Single(me!.Devices);
        Assert.True(device.Current);
        Assert.Equal("android", device.Platform);
        Assert.Equal("android device", device.Name);
    }

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/favorites")]
    [InlineData("/api/board")]
    [InlineData("/api/lines/131Y/stops")]
    [InlineData("/api/lines/131Y/live")]
    public async Task ApiWithoutKey_IsRejected(string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task UnknownKey_IsRejected()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.WithKey("not-a-real-key").GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task UnknownPlatform_IsRejected()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/devices", new RegisterDeviceRequest("toaster", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LinkCode_AddsAnotherDeviceToTheSameUser()
    {
        var phone = factory.WithKey((await RegisterAsync("android")).Key);
        var code = await (await phone.PostAsync("/api/me/link-codes", null)).Content.ReadFromJsonAsync<LinkCodeResponse>();

        var laptop = await RegisterAsync("web", code!.Code.ToLowerInvariant().Replace("-", " "));

        Assert.True(laptop.LinkedToExistingUser);
        var me = await phone.GetFromJsonAsync<MeResponse>("/api/me");
        Assert.Equal(["android", "web"], me!.Devices.Select(d => d.Platform));
        Assert.Matches("^[A-Z2-9]{3}-[A-Z2-9]{3}$", code.Code);
    }

    [Fact]
    public async Task LinkCode_WorksOnlyOnce()
    {
        var phone = factory.WithKey((await RegisterAsync()).Key);
        var code = await (await phone.PostAsync("/api/me/link-codes", null)).Content.ReadFromJsonAsync<LinkCodeResponse>();
        await RegisterAsync("web", code!.Code);

        var again = await factory.CreateClient().PostAsJsonAsync("/api/devices", new RegisterDeviceRequest("web", null, null, code.Code));

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task LinkCode_ExpiresAfterItsLifetime()
    {
        var phone = factory.WithKey((await RegisterAsync()).Key);
        var code = await (await phone.PostAsync("/api/me/link-codes", null)).Content.ReadFromJsonAsync<LinkCodeResponse>();
        factory.Clock.Advance(TimeSpan.FromMinutes(11));

        var late = await factory.CreateClient().PostAsJsonAsync("/api/devices", new RegisterDeviceRequest("web", null, null, code!.Code));

        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
    }

    [Fact]
    public async Task DeletingADevice_RevokesItsKey()
    {
        var phoneKey = (await RegisterAsync()).Key;
        var phone = factory.WithKey(phoneKey);
        var code = await (await phone.PostAsync("/api/me/link-codes", null)).Content.ReadFromJsonAsync<LinkCodeResponse>();
        var laptop = await RegisterAsync("web", code!.Code);

        var deleted = await phone.DeleteAsync($"/api/me/devices/{laptop.DeviceId}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.WithKey(laptop.Key).GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task OnlyDevice_CannotBeDeleted()
    {
        var registered = await RegisterAsync();
        var phone = factory.WithKey(registered.Key);

        var response = await phone.DeleteAsync($"/api/me/devices/{registered.DeviceId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task CannotDeleteAnotherUsersDevice()
    {
        var mine = factory.WithKey((await RegisterAsync()).Key);
        var theirs = await RegisterAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await mine.DeleteAsync($"/api/me/devices/{theirs.DeviceId}")).StatusCode);
    }
}

public sealed class InviteOnlyTests(InviteOnlyAppFactory factory) : IClassFixture<InviteOnlyAppFactory>
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("family-2025")]
    public async Task Register_WithoutTheRightInvite_IsForbidden(string? invite)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/devices", new RegisterDeviceRequest("web", null, invite, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithInvite_Works()
    {
        var client = await factory.CreateUserClientAsync(" " + InviteOnlyAppFactory.Invite + " ");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task LinkingADevice_DoesNotNeedTheInvite()
    {
        var phone = await factory.CreateUserClientAsync(InviteOnlyAppFactory.Invite);
        var code = await (await phone.PostAsync("/api/me/link-codes", null)).Content.ReadFromJsonAsync<LinkCodeResponse>();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/devices", new RegisterDeviceRequest("android", null, null, code!.Code));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}

public sealed class RegistrationRateLimitTests(StrictRateLimitAppFactory factory) : IClassFixture<StrictRateLimitAppFactory>
{
    [Fact]
    public async Task TooManyRegistrationsFromOneAddress_AreThrottled()
    {
        var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/devices", new RegisterDeviceRequest("web", null, null, null))).StatusCode);

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], statuses);
    }
}

public sealed class ProductionStartupTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("change-me")]
    public void Production_RefusesToStartWithoutRealInviteCodes(string? invite)
    {
        using var factory = new ProductionAppFactory(invite);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("invite", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Production_StartsWithAnInviteCode()
    {
        using var factory = new ProductionAppFactory("a-real-code");

        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/health")).StatusCode);
    }
}
