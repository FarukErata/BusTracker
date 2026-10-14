using System.Threading.RateLimiting;
using BusTracker.Api.Configuration;
using BusTracker.Api.Data;
using BusTracker.Api.Endpoints;
using BusTracker.Api.Favorites;
using BusTracker.Api.Ibb;
using BusTracker.Api.Lines;
using BusTracker.Api.Shapes;
using BusTracker.Api.Stops;
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
builder.Services.AddSingleton<LineDataService>();
builder.Services.AddSingleton<RouteShapeRepository>();
builder.Services.AddSingleton<RouteShapeSync>();
builder.Services.AddSingleton<StopIndexRepository>();
builder.Services.AddSingleton<StopIndexSync>();
builder.Services.AddSingleton<StopArrivalsService>();
builder.Services.AddHostedService<StaticDataSyncService>();
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<FavoriteRepository>();

builder.Services.AddHttpClient<IbbClient>((services, http) =>
{
    http.BaseAddress = new Uri(services.GetRequiredService<IOptions<BusTrackerOptions>>().Value.Ibb.BaseUrl);
    http.Timeout = Timeout.InfiniteTimeSpan;
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

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthEndpoints();
app.MapDeviceEndpoints();
app.MapFavoriteEndpoints();
app.MapStopEndpoints();
app.MapLineEndpoints();
app.MapLiveEndpoints();
app.MapFallbackToFile("{*path:regex(^(?!api/|dev/|health).*$)}", "index.html").AllowAnonymous();

if (!app.Environment.IsDevelopment() && !app.Services.GetRequiredService<IOptions<BusTrackerOptions>>().Value.Access.RequiresInvite)
    app.Logger.LogWarning("No invite codes configured: anyone who can reach this server can create a user");

app.Run();

public partial class Program;
