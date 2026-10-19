namespace BusTracker.Api.Tracking;

public sealed record Arrival(
    TrackedBus Bus,
    PlacedStop Stop,
    int StopsAway,
    double DistanceMeters,
    int? EtaSeconds,
    bool NotDeparted,
    bool HeavyTraffic = false);

public sealed record PassedBus(TrackedBus Bus, PlacedStop Stop, double MetersPast);

public sealed record StopArrivals(IReadOnlyList<Arrival> Approaching, IReadOnlyList<PassedBus> RecentlyPassed);

public sealed record EtaContext(HopTimes Hops, DateTimeOffset Now, bool UseHopTimes);

public sealed record EtaPair(int SpeedSeconds, int HopSeconds, bool HeavyTraffic);

public static class Arrivals
{
    private const double AtStopMeters = 30;
    private const double RecentlyPassedMeters = 3000;
    private const double MinEtaSpeedMps = 2;
    private const double ObservedSpeedWeight = 0.6;

    public static StopArrivals ForStop(
        string stopCode, string direction, IReadOnlyList<TrackedBus> buses, LineTracks tracks, EtaContext eta)
    {
        var placed = buses
            .Where(b => b.Bus.Direction == direction && b.IsUsable)
            .Select(b => (Bus: b, Track: tracks.For(b.Bus.RouteCode, b.Bus.Direction)))
            .Select(x => (x.Bus, x.Track, Stop: x.Track?.FindStop(stopCode)))
            .Where(x => x.Track is not null && x.Stop is not null)
            .Select(x => (x.Bus, Track: x.Track!, Stop: x.Stop!, Distance: x.Stop!.AlongMeters - x.Bus.AlongMeters!.Value))
            .ToList();

        var approaching = placed
            .Where(x => x.Distance >= -AtStopMeters)
            .Select(x => ToArrival(x.Bus, x.Track, x.Stop, x.Distance, eta))
            .OrderBy(a => a.DistanceMeters)
            .ToList();

        var passed = placed
            .Where(x => x.Distance < -AtStopMeters && x.Distance > -RecentlyPassedMeters)
            .Select(x => new PassedBus(x.Bus, x.Stop, -x.Distance))
            .OrderBy(p => p.MetersPast)
            .ToList();

        return new StopArrivals(approaching, passed);
    }

    public static int SpeedEstimate(double distanceMeters, double? observedSpeedMps, double scheduledSpeedMps)
    {
        if (distanceMeters <= AtStopMeters) return 0;
        var speed = observedSpeedMps is { } observed
            ? ObservedSpeedWeight * observed + (1 - ObservedSpeedWeight) * scheduledSpeedMps
            : scheduledSpeedMps;
        return (int)Math.Round(distanceMeters / Math.Max(speed, MinEtaSpeedMps));
    }

    public static EtaPair EstimateBoth(TrackedBus bus, RouteTrack track, PlacedStop stop, HopTimes hops, DateTimeOffset now)
    {
        var distance = Math.Max(0, stop.AlongMeters - bus.AlongMeters!.Value);
        var byHops = Estimate(bus, track, stop, hops, now);
        return new EtaPair(SpeedEstimate(distance, bus.SpeedMps, track.ScheduledSpeedMps), byHops.Seconds, byHops.HeavyTraffic);
    }

    public static TravelEstimate Estimate(TrackedBus bus, RouteTrack track, PlacedStop stop, HopTimes hops, DateTimeOffset now)
    {
        var from = bus.AlongMeters!.Value;
        if (stop.AlongMeters - from <= AtStopMeters) return new TravelEstimate(0, false);

        var travel = TravelTime.Between(track, from, stop.AlongMeters, bus.SpeedMps, hops, now);
        var sinceReport = Math.Max(0, (now - bus.Bus.ReportedAt).TotalSeconds);
        return travel with { Seconds = (int)Math.Max(0, Math.Round(travel.Seconds - sinceReport)) };
    }

    private static Arrival ToArrival(TrackedBus bus, RouteTrack track, PlacedStop stop, double distance, EtaContext eta)
    {
        var notDeparted = bus.Status == BusStatus.AtTerminal;
        var estimate = notDeparted ? null : EstimateBoth(bus, track, stop, eta.Hops, eta.Now);
        return new Arrival(
            bus,
            stop,
            track.StopsUntil(bus.AlongMeters!.Value, stop.AlongMeters),
            Math.Max(0, distance),
            estimate is null ? null : eta.UseHopTimes ? estimate.HopSeconds : estimate.SpeedSeconds,
            notDeparted,
            estimate?.HeavyTraffic ?? false);
    }
}
