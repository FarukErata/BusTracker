using BusTracker.Api.Configuration;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Endpoints;

public static class AppLinkEndpoints
{
    public static IEndpointRouteBuilder MapAppLinkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/assetlinks.json", GetAssetLinks).AllowAnonymous();
        return app;
    }

    private static IResult GetAssetLinks(IOptions<BusTrackerOptions> options)
    {
        var android = options.Value.AndroidApp;
        return Results.Json(new[]
        {
            new
            {
                relation = new[] { "delegate_permission/common.handle_all_urls" },
                target = new
                {
                    @namespace = "android_app",
                    package_name = android.PackageName,
                    sha256_cert_fingerprints = android.CertificateFingerprints,
                },
            },
        });
    }
}
