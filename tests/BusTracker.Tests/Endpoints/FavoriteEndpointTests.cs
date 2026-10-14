using System.Net;
using System.Net.Http.Json;
using BusTracker.Api.Endpoints;
using BusTracker.Api.Favorites;

namespace BusTracker.Tests.Endpoints;

public sealed class FavoriteEndpointTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    private const string SehitSahinbey = "402011";

    [Fact]
    public async Task AddLine_WithAndWithoutDirection()
    {
        var client = await factory.CreateUserClientAsync();

        var both = await client.PostAsJsonAsync("/api/favorites/lines", new AddFavoriteLineRequest("131y", null, "Saved line"));
        var oneWay = await client.PostAsJsonAsync("/api/favorites/lines", new AddFavoriteLineRequest("131Y", "g", null));

        Assert.Equal(HttpStatusCode.Created, both.StatusCode);
        Assert.Equal(HttpStatusCode.Created, oneWay.StatusCode);
        var list = await client.GetFromJsonAsync<FavoritesResponse>("/api/favorites");
        Assert.Equal([null, "G"], list!.Lines.Select(l => l.Favorite.Direction));
        Assert.Equal("Saved line", list.Lines[0].Favorite.Label);
        Assert.Equal("YEŞİLTEPE SON DURAK → KARTAL", list.Lines[0].Directions["G"]);
        Assert.Equal("KARTAL → YEŞİLTEPE SON DURAK", list.Lines[0].Directions["D"]);
    }

    [Fact]
    public async Task AddLine_SameLineAndDirectionTwice_IsAConflict()
    {
        var client = await factory.CreateUserClientAsync();
        await client.PostAsJsonAsync("/api/favorites/lines", new AddFavoriteLineRequest("131Y", null, null));

        var again = await client.PostAsJsonAsync("/api/favorites/lines", new AddFavoriteLineRequest("131Y", "", null));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Theory]
    [InlineData("NOSUCH", null)]
    [InlineData("131Y", "X")]
    public async Task AddLine_UnknownLineOrDirection_IsNotFound(string line, string? direction)
    {
        var client = await factory.CreateUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/favorites/lines", new AddFavoriteLineRequest(line, direction, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddStop_OnItsOwn_ListsEveryLineServingIt()
    {
        var client = await factory.CreateUserClientAsync();

        var response = await client.PostAsJsonAsync("/api/favorites/stops", new AddFavoriteStopRequest(SehitSahinbey, "  Saved stop  "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stop = Assert.Single((await client.GetFromJsonAsync<FavoritesResponse>("/api/favorites"))!.Stops);
        Assert.Equal(("ŞEHİT ŞAHİNBEY", "Saved stop"), (stop.Favorite.StopName, stop.Favorite.Label));
        Assert.Equal(["131Y", "14ÇK", "14ŞB", "9S", "ÇM42"], stop.Lines.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AddStop_TwiceOrUnknown_IsRejected()
    {
        var client = await factory.CreateUserClientAsync();
        await client.PostAsJsonAsync("/api/favorites/stops", new AddFavoriteStopRequest(SehitSahinbey, null));

        var again = await client.PostAsJsonAsync("/api/favorites/stops", new AddFavoriteStopRequest(SehitSahinbey, null));
        var unknown = await client.PostAsJsonAsync("/api/favorites/stops", new AddFavoriteStopRequest("999999", null));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task DeleteStop()
    {
        var client = await factory.CreateUserClientAsync();
        var added = await (await client.PostAsJsonAsync("/api/favorites/stops", new AddFavoriteStopRequest(SehitSahinbey, null)))
            .Content.ReadFromJsonAsync<FavoriteStop>();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/favorites/stops/{added!.Id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<FavoritesResponse>("/api/favorites"))!.Stops);
    }

    [Fact]
    public async Task UsersCannotSeeOrDeleteEachOthersFavorites()
    {
        var alice = await factory.CreateUserClientAsync();
        var bob = await factory.CreateUserClientAsync();
        var added = await (await alice.PostAsJsonAsync("/api/favorites/lines", new AddFavoriteLineRequest("131Y", null, null)))
            .Content.ReadFromJsonAsync<FavoriteLine>();

        Assert.Empty((await bob.GetFromJsonAsync<FavoritesResponse>("/api/favorites"))!.Lines);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/favorites/lines/{added!.Id}")).StatusCode);
        Assert.Single((await alice.GetFromJsonAsync<FavoritesResponse>("/api/favorites"))!.Lines);
    }
}
