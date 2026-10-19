using BusTracker.Api.Users;

namespace BusTracker.Api.Tracing;

public sealed class RequestTracingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        using var activity = Telemetry.Request("request");
        var failed = false;
        try
        {
            await next(context);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            if (activity is not null) Describe(activity, context, failed);
        }
    }

    private static void Describe(System.Diagnostics.Activity activity, HttpContext context, bool failed)
    {
        activity.SetTag("method", context.Request.Method);
        activity.SetTag("path", context.Request.Path.Value);
        activity.SetTag("route", (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText);
        activity.SetTag("status", failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode);
        if (context.User.Identity?.IsAuthenticated != true) return;
        activity.SetTag("user", context.User.UserId());
        activity.SetTag("device", context.User.DeviceId());
    }
}
