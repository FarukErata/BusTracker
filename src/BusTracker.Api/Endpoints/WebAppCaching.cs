using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace BusTracker.Api.Endpoints;

public static class WebAppCaching
{
    private const string HashedAssets = "/assets";
    private const string Forever = "public, max-age=31536000, immutable";
    private const string AlwaysCheck = "no-cache";

    public static void Apply(StaticFileResponseContext context)
    {
        var path = context.Context.Request.Path;
        context.Context.Response.Headers[HeaderNames.CacheControl] =
            path.StartsWithSegments(HashedAssets) ? Forever : AlwaysCheck;
    }
}
