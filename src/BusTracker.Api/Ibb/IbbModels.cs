namespace BusTracker.Api.Ibb;

public sealed record LiveBus(
    string DoorNo,
    double Lat,
    double Lng,
    string LineCode,
    string RouteCode,
    string Direction,
    string Variant,
    string Destination,
    DateTimeOffset ReportedAt,
    string NearestStopCode)
{
    public bool IsMainVariant => Variant == "D0";
}

public sealed record FleetVehicle(string DoorNo, double Lat, double Lng, DateTimeOffset ReportedAt);

public sealed record RouteStop(
    string LineCode,
    string Direction,
    string DirectionName,
    int Seq,
    string StopCode,
    string Name,
    double Lat,
    double Lng,
    string District);

public sealed record PlannedTrip(string LineCode, string RouteCode, string Direction, string DayType, TimeOnly Departure);

public sealed record IbbAnnouncement(string LineCode, string Kind, string PostedAt, string Message);

public class IbbException(string message) : Exception(message);

public sealed class IbbFaultException(string message) : IbbException(message);

public sealed class IbbRateLimitedException() : IbbException("İBB rate limit exceeded");
