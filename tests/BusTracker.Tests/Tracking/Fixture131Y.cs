using System.Text;
using BusTracker.Api.Configuration;
using BusTracker.Api.Ibb;
using BusTracker.Api.Shapes;
using BusTracker.Api.Tracking;

namespace BusTracker.Tests.Tracking;

public static class Fixture131Y
{
    public static readonly DateTimeOffset SnapshotTime = new(2026, 10, 14, 13, 7, 0, TimeSpan.FromHours(3));

    public static readonly TrackingOptions Options = new();

    public static readonly IReadOnlyList<RouteStop> Stops =
        IbbParsers.ParseRouteStops(SoapEnvelope.ReadResult("DurakDetay_GYY_wYonAdi", Fixture.Read("route-stops-131Y.xml")));

    public static readonly IReadOnlyList<LiveBus> Buses =
        IbbParsers.ParseLiveBuses(SoapEnvelope.ReadResult("GetHatOtoKonum_json", Fixture.Read("live-131Y.xml")));

    public static readonly IReadOnlyList<RouteShape> Shapes = LoadShapes();

    public static LineTracks Tracks() => LineTracks.Build(Stops, Shapes, Options.StopOnRouteMeters);

    public static LiveBus Bus(string doorNo) => Buses.Single(b => b.DoorNo == doorNo);

    public static LiveBus MovedAlong(LiveBus bus, RouteTrack track, double alongMeters, TimeSpan later)
    {
        var point = track.Geometry.PointAt(alongMeters);
        return bus with { Lat = point.Lat, Lng = point.Lng, ReportedAt = bus.ReportedAt + later };
    }

    private static IReadOnlyList<RouteShape> LoadShapes()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Fixture.Read("shapes-131Y.geojson")));
        return ReadAll(stream).GetAwaiter().GetResult();
    }

    private static async Task<IReadOnlyList<RouteShape>> ReadAll(Stream stream)
    {
        var shapes = new List<RouteShape>();
        await foreach (var feature in GeoJsonFeatureReader.ReadFeaturesAsync(stream, CancellationToken.None))
        {
            using (feature)
                shapes.Add(RouteShapeParser.Parse(feature, 5)!);
        }
        return shapes;
    }
}
