namespace BusTracker.Api.Tracking;

public sealed record TravelEstimate(int Seconds, bool HeavyTraffic);

public static class TravelTime
{
    public const double DefaultSpeedMps = 4.5;
    public const double HeavyTrafficSpeedMps = 2.2;
    public const double MinHeavyTrafficEvidenceMeters = 400;
    public const double TrafficLookaheadMeters = 2000;
    private const double MinSpeedMps = 1.5;
    private const double OwnSpeedWeight = 0.6;

    public static TravelEstimate Between(
        RouteTrack track, double fromAlong, double toAlong, double? busSpeedMps, HopTimes hops, DateTimeOffset now)
    {
        if (toAlong <= fromAlong) return new TravelEstimate(0, false);

        var fallback = FallbackSpeed(busSpeedMps);
        double seconds = 0, measuredMeters = 0, measuredSeconds = 0;
        foreach (var piece in Pieces(track, fromAlong, toAlong))
        {
            var meters = piece.End - piece.Start;
            if (piece.FromStop is { } from && hops.RecentSeconds(track.RouteCode, from, now) is { } hopSeconds)
            {
                var share = hopSeconds * meters / piece.HopLength;
                seconds += share;
                measuredMeters += meters;
                measuredSeconds += share;
            }
            else
            {
                seconds += meters / fallback;
            }
        }

        var heavy = measuredMeters >= MinHeavyTrafficEvidenceMeters && measuredMeters / measuredSeconds < HeavyTrafficSpeedMps;
        return new TravelEstimate((int)Math.Round(seconds), heavy);
    }

    public static bool HeavyTrafficAhead(RouteTrack track, double along, double? busSpeedMps, HopTimes hops, DateTimeOffset now) =>
        Between(track, along, Math.Min(along + TrafficLookaheadMeters, track.Geometry.LengthMeters), busSpeedMps, hops, now).HeavyTraffic;

    public static double FallbackSpeed(double? busSpeedMps) =>
        Math.Max(MinSpeedMps, busSpeedMps is { } own ? OwnSpeedWeight * own + (1 - OwnSpeedWeight) * DefaultSpeedMps : DefaultSpeedMps);

    private readonly record struct Piece(int? FromStop, double Start, double End, double HopLength);

    private static IEnumerable<Piece> Pieces(RouteTrack track, double fromAlong, double toAlong)
    {
        var stops = track.Stops;
        if (stops.Count == 0)
        {
            yield return new Piece(null, fromAlong, toAlong, toAlong - fromAlong);
            yield break;
        }

        var boundaries = new double[stops.Count];
        for (var i = 0; i < stops.Count; i++)
            boundaries[i] = i == 0 ? stops[0].AlongMeters : Math.Max(boundaries[i - 1], stops[i].AlongMeters);

        if (fromAlong < boundaries[0])
            yield return new Piece(null, fromAlong, Math.Min(toAlong, boundaries[0]), boundaries[0] - fromAlong);

        for (var i = 0; i + 1 < stops.Count; i++)
        {
            var start = Math.Max(fromAlong, boundaries[i]);
            var end = Math.Min(toAlong, boundaries[i + 1]);
            var length = boundaries[i + 1] - boundaries[i];
            if (end > start && length > 0) yield return new Piece(stops[i].Index, start, end, length);
        }

        var last = boundaries[^1];
        if (toAlong > last)
            yield return new Piece(null, Math.Max(fromAlong, last), toAlong, toAlong - Math.Max(fromAlong, last));
    }
}
