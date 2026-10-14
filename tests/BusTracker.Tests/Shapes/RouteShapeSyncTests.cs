using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BusTracker.Api.Configuration;
using BusTracker.Api.Shapes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BusTracker.Tests.Shapes;

public sealed class RouteShapeSyncTests : IAsyncLifetime
{
    private const string Url = "https://example.test/shapes.geojson";

    private readonly FakeOpenDataHandler _handler = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero));
    private TestDatabase _db = null!;
    private RouteShapeRepository _repository = null!;
    private RouteShapeSync _sync = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(new StaticDataOptions { RouteShapesUrl = Url, SimplifyToleranceMeters = 5 });
        _repository = new RouteShapeRepository(_db.Database);
        _sync = new RouteShapeSync(
            new OpenDataClient(new HttpClient(_handler)), _repository, Options.Create(_db.Options), _clock,
            NullLogger<RouteShapeSync>.Instance);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task FirstSync_ImportsEveryShape()
    {
        var outcome = await _sync.SyncAsync(CancellationToken.None);

        Assert.Equal(SyncOutcome.Imported, outcome);
        Assert.Equal(1, _handler.Downloads);
        var shapes = await _repository.GetForLineAsync("131Y", CancellationToken.None);
        Assert.Equal(["131Y_D_D0", "131Y_G_D0", "131Y_G_D1895"], shapes.Select(s => s.RouteCode).Order());
        var state = await _repository.GetSourceStateAsync(CancellationToken.None);
        Assert.Equal("\"v1\"", state!.ETag);
        Assert.Equal(3, state.ItemCount);
    }

    [Fact]
    public async Task SameETag_SkipsDownload()
    {
        await _sync.SyncAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromDays(1));

        var outcome = await _sync.SyncAsync(CancellationToken.None);

        Assert.Equal(SyncOutcome.Unchanged, outcome);
        Assert.Equal(1, _handler.Downloads);
        Assert.Equal(2, _handler.HeadRequests);
        var state = await _repository.GetSourceStateAsync(CancellationToken.None);
        Assert.Equal(_clock.GetUtcNow(), state!.CheckedAt);
    }

    [Fact]
    public async Task NewETag_DownloadsAgain()
    {
        await _sync.SyncAsync(CancellationToken.None);
        _handler.ETag = "\"v2\"";

        var outcome = await _sync.SyncAsync(CancellationToken.None);

        Assert.Equal(SyncOutcome.Imported, outcome);
        Assert.Equal(2, _handler.Downloads);
        Assert.Equal("\"v2\"", (await _repository.GetSourceStateAsync(CancellationToken.None))!.ETag);
    }

    [Fact]
    public async Task FailedDownload_KeepsPreviousShapes()
    {
        await _sync.SyncAsync(CancellationToken.None);
        _handler.ETag = "\"v3\"";
        _handler.Body = """{"features":[{"type":"Feature","properties":{"GUZERGAH_KODU":"X_G_D0"},"geometry":""";

        await Assert.ThrowsAnyAsync<Exception>(() => _sync.SyncAsync(CancellationToken.None));

        Assert.Equal(3, await _repository.CountAsync(CancellationToken.None));
        Assert.Equal("\"v1\"", (await _repository.GetSourceStateAsync(CancellationToken.None))!.ETag);
    }

    [Fact]
    public async Task ShrunkenFile_IsRejectedAndPreviousShapesKept()
    {
        await _sync.SyncAsync(CancellationToken.None);
        _handler.ETag = "\"v4\"";
        _handler.Body = """{"features":[]}""";

        await Assert.ThrowsAsync<InvalidDataException>(() => _sync.SyncAsync(CancellationToken.None));

        Assert.Equal(3, await _repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StoredShapes_DecodeToTheSimplifiedGeometry()
    {
        await _sync.SyncAsync(CancellationToken.None);

        var main = (await _repository.GetForLineAsync("131Y", CancellationToken.None)).Single(s => s.RouteCode == "131Y_D_D0");

        Assert.InRange(main.Points.Count, 2, 465);
        Assert.Equal(40.88670, main.Points[0].Lat, 3);
        Assert.Equal(27949.44, main.LengthMeters, 2);
    }

    private sealed class FakeOpenDataHandler : HttpMessageHandler
    {
        public string ETag { get; set; } = "\"v1\"";
        public string Body { get; set; } = Fixture.Read("shapes-131Y.geojson");
        public int HeadRequests { get; private set; }
        public int Downloads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(Body);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.ETag = EntityTagHeaderValue.Parse(ETag);

            if (request.Method == HttpMethod.Head)
            {
                HeadRequests++;
                response.Content = new ByteArrayContent([]);
            }
            else
            {
                Downloads++;
                response.Content = new ByteArrayContent(bytes);
            }

            response.Content.Headers.ContentLength = bytes.Length;
            response.Content.Headers.LastModified = new DateTimeOffset(2026, 3, 18, 10, 40, 5, TimeSpan.Zero);
            return Task.FromResult(response);
        }
    }
}
