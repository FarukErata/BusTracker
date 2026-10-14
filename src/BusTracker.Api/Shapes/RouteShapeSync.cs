using System.Runtime.CompilerServices;
using BusTracker.Api.Configuration;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Shapes;

public enum SyncOutcome { Unchanged, Imported }

public sealed class RouteShapeSync(
    OpenDataClient openData,
    RouteShapeRepository repository,
    IOptions<BusTrackerOptions> options,
    TimeProvider clock,
    ILogger<RouteShapeSync> logger)
{
    private readonly StaticDataOptions _options = options.Value.StaticData;

    public async Task<SyncOutcome> SyncAsync(CancellationToken ct)
    {
        var remote = await openData.GetVersionAsync(_options.RouteShapesUrl, ct);
        var stored = await repository.GetSourceStateAsync(ct);
        var now = clock.GetUtcNow();

        if (stored is not null && await IsUpToDateAsync(remote, stored, ct))
        {
            await repository.SaveSourceStateAsync(stored with { CheckedAt = now }, ct);
            logger.LogInformation("Route shapes unchanged ({ETag})", remote.ETag);
            return SyncOutcome.Unchanged;
        }

        var count = await DownloadAsync(remote, stored, ct);

        await repository.SaveSourceStateAsync(
            new DataSourceState(RouteShapeRepository.SourceName, remote.ETag, remote.LastModified, remote.Size,
                now, clock.GetUtcNow(), count), ct);
        logger.LogInformation("Imported {Count} route shapes", count);
        return SyncOutcome.Imported;
    }

    private async Task<bool> IsUpToDateAsync(SourceVersion remote, DataSourceState stored, CancellationToken ct) =>
        remote.SameAs(stored.Version) && await repository.CountAsync(ct) > 0;

    private async Task<int> DownloadAsync(SourceVersion remote, DataSourceState? stored, CancellationToken ct)
    {
        logger.LogInformation("Route shapes changed ({Old} → {New}); downloading {Size} bytes",
            stored?.ETag ?? "none", remote.ETag, remote.Size);
        await using var stream = await openData.OpenAsync(_options.RouteShapesUrl, ct);
        var shapes = await ParseAsync(stream, ct).ToListAsync(ct);
        var minimumCount = Math.Max(1, (stored?.ItemCount ?? 0) / 2);
        return await repository.ReplaceAllAsync(shapes.ToAsyncEnumerable(), minimumCount, ct);
    }

    private async IAsyncEnumerable<RouteShape> ParseAsync(Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var feature in GeoJsonFeatureReader.ReadFeaturesAsync(stream, ct))
        {
            using (feature)
            {
                if (RouteShapeParser.Parse(feature, _options.SimplifyToleranceMeters) is { } shape)
                    yield return shape;
            }
        }
    }
}
