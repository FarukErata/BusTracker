using System.ComponentModel.DataAnnotations;

namespace BusTracker.Api.Configuration;

public sealed class BusTrackerOptions
{
    public const string SectionName = "BusTracker";

    [Required]
    public string DatabasePath { get; init; } = "App_Data/bustracker.db";

    public IbbOptions Ibb { get; init; } = new();
    public PollingOptions Polling { get; init; } = new();
    public TrackingOptions Tracking { get; init; } = new();
    public StaticDataOptions StaticData { get; init; } = new();
    public AccessOptions Access { get; init; } = new();
    public RequestLogOptions RequestLog { get; init; } = new();
}

public sealed class RequestLogOptions
{
    public bool Enabled { get; init; } = true;

    public string? Directory { get; init; }

    [Range(1, 365)]
    public int RetentionDays { get; init; } = 14;
}

public sealed class IbbOptions
{
    [Required, Url]
    public string BaseUrl { get; init; } = "https://api.ibb.gov.tr/iett/";

    [Range(5, 120)]
    public int TimeoutSeconds { get; init; } = 30;
}

public sealed class PollingOptions
{
    [Range(60, 600)]
    public int FleetIntervalSeconds { get; init; } = 60;

    [Range(10, 100)]
    public int CallsPerHour { get; init; } = 90;

    [Range(0, 100)]
    public int RosterCallsPerHour { get; init; } = 60;

    [Range(1, 20)]
    public int RosterCallsPerMinute { get; init; } = 10;

    [Range(0, 100)]
    public int FleetReservedCalls { get; init; } = 45;

    [Range(1, 120)]
    public int RosterMaxAgeMinutes { get; init; } = 60;

    [Range(1, 168)]
    public int MemoryTrustHours { get; init; } = 72;

    [Range(1, 60)]
    public int RosterRecheckMinutes { get; init; } = 3;

    public int[] BackoffMinutes { get; init; } = [5, 10, 20, 30];
}

public sealed class TrackingOptions
{
    [Range(30, 900)]
    public int StaleAfterSeconds { get; init; } = 180;

    [Range(50, 5000)]
    public int OffRouteMeters { get; init; } = 200;

    [Range(10, 200)]
    public int StopOnRouteMeters { get; init; } = 40;

    [Range(20, 1000)]
    public int TerminalMeters { get; init; } = 150;
}

public sealed class StaticDataOptions
{
    [Range(1, 168)]
    public int RefreshHours { get; init; } = 24;

    public bool SyncEnabled { get; init; } = true;

    [Required, Url]
    public string RouteShapesUrl { get; init; } =
        "https://data.ibb.gov.tr/dataset/b48d2095-851c-413c-8d36-87d2310a22b5/resource/4ccb4d29-c2b6-414a-b324-d2c9962b18e2/download/iett-hat-guzergahlar-verisi.geojson";

    [Range(0.0, 50.0)]
    public double SimplifyToleranceMeters { get; init; } = 5;

    [Range(1, 60)]
    public int DownloadTimeoutMinutes { get; init; } = 15;
}

public sealed class AccessOptions
{
    public string[] InviteCodes { get; init; } = [];

    [Range(1, 60)]
    public int LinkCodeMinutes { get; init; } = 10;

    [Range(1, 1000)]
    public int RegistrationsPerIpPer10Minutes { get; init; } = 10;

    public bool RequiresInvite => InviteCodes.Any(c => !string.IsNullOrWhiteSpace(c));
}
