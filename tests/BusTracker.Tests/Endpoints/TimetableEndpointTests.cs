using System.Net.Http.Json;
using BusTracker.Api.Endpoints;
using BusTracker.Api.Timetables;
using Microsoft.Extensions.DependencyInjection;

namespace BusTracker.Tests.Endpoints;

public sealed class TimetableEndpointTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    private async Task<HttpClient> ClientAsync()
    {
        var client = await factory.CreateUserClientAsync();
        await factory.Services.GetRequiredService<AnnouncementService>().RefreshAsync(CancellationToken.None);
        return client;
    }

    [Fact]
    public async Task Timetable_ListsTodaysTripsPerDirectionWithCancellations()
    {
        var client = await ClientAsync();

        var timetable = await client.GetFromJsonAsync<TimetableResponse>("/api/lines/131Y/timetable");

        Assert.True(timetable!.Available);
        Assert.Equal(("I", "I", false), (timetable.DayType, timetable.TodayDayType, timetable.Holiday));
        var g = Assert.Single(timetable.Directions, d => d.Direction == "G");
        var d = Assert.Single(timetable.Directions, d => d.Direction == "D");
        Assert.Equal(("YEŞİLTEPE SON DURAK", "KARTAL"), (g.FirstStop, d.FirstStop));
        Assert.True(g.Trips.Single(t => t.Time == "13:25").Cancelled);
        Assert.False(g.Trips.Single(t => t.Time == "13:40").Cancelled);
        Assert.True(d.Trips.Single(t => t.Time == "13:40").Cancelled);
        Assert.Equal("00:20", g.Trips[^1].Time);
        Assert.Equal(3, timetable.Announcements.Count);
    }

    [Fact]
    public async Task Timetable_ForAnotherDay_HasNoCancellations()
    {
        var client = await ClientAsync();

        var sunday = await client.GetFromJsonAsync<TimetableResponse>("/api/lines/131Y/timetable?day=p");

        Assert.Equal("P", sunday!.DayType);
        Assert.Equal(["08:00"], sunday.Directions.Single(d => d.Direction == "G").Trips.Select(t => t.Time));
        Assert.All(sunday.Directions.SelectMany(d => d.Trips), t => Assert.False(t.Cancelled));
    }

    [Fact]
    public async Task Timetable_IsFetchedFromIbbOncePerDay()
    {
        var client = await ClientAsync();
        await client.GetAsync("/api/stops/402011/arrivals");
        var calls = factory.CallsTo("GetPlanlananSeferSaati_json");

        await client.GetAsync("/api/lines/131Y/timetable");
        await client.GetAsync("/api/lines/131Y/live");
        await client.GetAsync("/api/stops/402011/arrivals");

        Assert.Equal(calls, factory.CallsTo("GetPlanlananSeferSaati_json"));
    }

    [Fact]
    public async Task StopArrivals_IncludePlannedTripsAndAnnouncements()
    {
        var client = await ClientAsync();

        var view = await client.GetFromJsonAsync<StopArrivalsView>("/api/stops/402011/arrivals");

        var line = Assert.Single(view!.Lines, l => l.Line == "131Y");
        Assert.NotNull(line.Planned);
        Assert.Equal("YEŞİLTEPE SON DURAK", line.Planned!.FirstStop);
        Assert.NotEmpty(line.Planned.Next);
        Assert.All(line.Planned.Next, t => Assert.True(t.AtStop >= t.Departs));
        Assert.Contains(view.Announcements, a => a is { Line: "131Y", Kind: "cancelled", Direction: "G", Departure: "13:25" });
        Assert.Contains(view.Announcements, a => a is { Line: "131Y", Kind: "notice" });
    }

    [Fact]
    public async Task LineArrivals_IncludeThePlanForThatStop()
    {
        var client = await ClientAsync();

        var arrivals = await client.GetFromJsonAsync<StopArrivalsResponse>("/api/lines/131Y/arrivals?stop=402011&dir=G");

        Assert.Equal("G", arrivals!.Planned!.Direction);
        Assert.Equal(3, arrivals.Announcements.Count);
    }

    [Fact]
    public async Task HomeAnnouncements_CoverFavouriteLinesAndTheLinesOfFavouriteStops()
    {
        var client = await ClientAsync();

        var result = await client.GetFromJsonAsync<AnnouncementsResponse>("/api/announcements?lines=500t&stops=402011");

        Assert.Contains(result!.Announcements, a => a.Line == "500T");
        Assert.Contains(result.Announcements, a => a.Line == "131Y");
    }
}
