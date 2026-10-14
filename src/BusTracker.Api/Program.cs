using BusTracker.Api.Configuration;
using BusTracker.Api.Data;
using BusTracker.Api.Endpoints;
using BusTracker.Api.Ibb;
using BusTracker.Api.Lines;
using BusTracker.Api.Shapes;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddHostedService<RouteShapeSyncService>();

builder.Services.AddHttpClient<IbbClient>((services, http) =>
{
    var ibb = services.GetRequiredService<IOptions<BusTrackerOptions>>().Value.Ibb;
    http.BaseAddress = new Uri(ibb.BaseUrl);
    http.Timeout = TimeSpan.FromSeconds(ibb.TimeoutSeconds);
});

builder.Services.AddHttpClient<OpenDataClient>((services, http) =>
    http.Timeout = TimeSpan.FromMinutes(
        services.GetRequiredService<IOptions<BusTrackerOptions>>().Value.StaticData.DownloadTimeoutMinutes));

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
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapGet("/", () => Results.Redirect("/dev/"));
}

app.MapHealthEndpoints();
app.MapLineEndpoints();

app.Run();

public partial class Program;
