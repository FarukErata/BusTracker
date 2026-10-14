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
    public int LineIntervalSeconds { get; init; } = 60;

    [Range(1, 20)]
    public int FleetCallsPerMinute { get; init; } = 10;

    [Range(1, 60)]
    public int HotLineTtlMinutes { get; init; } = 5;

    public int[] BackoffMinutes { get; init; } = [2, 4, 8, 15];
}

public sealed class TrackingOptions
{
    [Range(30, 900)]
    public int StaleAfterSeconds { get; init; } = 180;

    [Range(50, 5000)]
    public int OffRouteMeters { get; init; } = 500;

    [Range(0.0, 1.0)]
    public double TerminalWaitFraction { get; init; } = 0.2;
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
