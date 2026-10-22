using System.Threading.RateLimiting;
using BusTracker.Api.Configuration;
using BusTracker.Api.Data;
using BusTracker.Api.Endpoints;
using BusTracker.Api.Favorites;
using BusTracker.Api.Ibb;
using BusTracker.Api.Rail;
using BusTracker.Api.Lines;
using BusTracker.Api.Shapes;
using BusTracker.Api.Stops;
using BusTracker.Api.Timetables;
using BusTracker.Api.Tracing;
using BusTracker.Api.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var webRoot = builder.Configuration[WebHostDefaults.WebRootKey] is { Length: > 0 } configuredRoot
    ? configuredRoot
    : Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(webRoot);
builder.Environment.WebRootPath = webRoot;
builder.Environment.WebRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webRoot);

builder.Services
    .AddOptions<BusTrackerOptions>()
    .Bind(builder.Configuration.GetSection(BusTrackerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Database>();
builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddSingleton<FleetCallGate>();
builder.Services.AddHostedService<IbbCallLog>();
builder.Services.AddSingleton<RequestLog>();
builder.Services.AddHostedService(services => services.GetRequiredService<RequestLog>());
builder.Services.AddSingleton<FleetFeed>();
builder.Services.AddSingleton<BusLineMemory>();
builder.Services.AddSingleton<BusTracker.Api.Tracking.HopTimes>();
builder.Services.AddHostedService<HopTimeLog>();
builder.Services.AddSingleton<EtaAccuracy>();
builder.Services.AddHostedService(services => services.GetRequiredService<EtaAccuracy>());
builder.Services.AddSingleton<LineDataService>();
builder.Services.AddSingleton<RouteShapeRepository>();
builder.Services.AddSingleton<RouteShapeSync>();
builder.Services.AddSingleton<StopIndexRepository>();
builder.Services.AddSingleton<StopIndexSync>();
builder.Services.AddSingleton<StopArrivalsService>();
builder.Services.AddSingleton<TimetableService>();
builder.Services.AddSingleton<AnnouncementService>();
builder.Services.AddHostedService(services => services.GetRequiredService<AnnouncementService>());
builder.Services.AddSingleton<LinePlans>();
builder.Services.AddSingleton<RailCatalog>();
builder.Services.AddSingleton<RailTimetables>();
builder.Services.AddSingleton<RailBoard>();
builder.Services.AddHostedService<RailSyncService>();
builder.Services.AddHostedService<StaticDataSyncService>();
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<FavoriteRepository>();

builder.Services.AddHttpClient<IbbClient>((services, http) =>
{
    http.BaseAddress = new Uri(services.GetRequiredService<IOptions<BusTrackerOptions>>().Value.Ibb.BaseUrl);
    http.Timeout = Timeout.InfiniteTimeSpan;
});

builder.Services.AddHttpClient<MetroClient>((services, http) =>
{
    http.BaseAddress = new Uri(services.GetRequiredService<IOptions<BusTrackerOptions>>().Value.Ibb.MetroBaseUrl);
    http.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpClient<OpenDataClient>((services, http) =>
    http.Timeout = TimeSpan.FromMinutes(
        services.GetRequiredService<IOptions<BusTrackerOptions>>().Value.StaticData.DownloadTimeoutMinutes));

builder.Services
    .AddAuthentication(DeviceKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DeviceKeyAuthenticationHandler>(DeviceKeyAuthenticationHandler.SchemeName, null);

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(DeviceEndpoints.RegistrationRateLimit, context =>
    {
        var limit = context.RequestServices.GetRequiredService<IOptions<BusTrackerOptions>>().Value.Access.RegistrationsPerIpPer10Minutes;
        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(10) });
    });
});

builder.Services.AddProblemDetails();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(System.Net.IPAddress.Loopback);
    foreach (var network in builder.Configuration.GetSection("TrustedProxyNetworks").Get<string[]>() ?? [])
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
});

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    var devPage = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.Combine(app.Environment.ContentRootPath, "DevPage"));
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = devPage, RequestPath = "/dev" });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = devPage, RequestPath = "/dev" });
}

var webApp = new StaticFileOptions { OnPrepareResponse = WebAppCaching.Apply };
app.UseDefaultFiles();
app.UseStaticFiles(webApp);
app.UseRouting();
app.UseMiddleware<RequestTracingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthEndpoints();
app.MapAppLinkEndpoints();
app.MapDeviceEndpoints();
app.MapFavoriteEndpoints();
app.MapStopEndpoints();
app.MapLineEndpoints();
app.MapLiveEndpoints();
app.MapTimetableEndpoints();
app.MapRailEndpoints();
app.MapStatsEndpoints();
app.MapFallbackToFile("{*path:regex(^(?!api/|dev/|health).*$)}", "index.html", webApp).AllowAnonymous();

var access = app.Services.GetRequiredService<IOptions<BusTrackerOptions>>().Value.Access;
if (!app.Environment.IsDevelopment())
{
    if (access.HasPlaceholderInvite)
        throw new InvalidOperationException("Replace the example invite code (change-me) in .env with your own codes.");
    if (!access.RequiresInvite && !access.AllowOpenRegistration)
        throw new InvalidOperationException(
            "No invite codes configured. Set BusTracker__Access__InviteCodes__0, or BusTracker__Access__AllowOpenRegistration=true to let anyone register.");
    if (!access.RequiresInvite)
        app.Logger.LogWarning("Open registration: anyone who can reach this server can create a user");
}

app.Run();
