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
        int? status = null;
        try
        {
            await next(context);
        }
        catch (Exception e)
        {
            status = context.RequestAborted.IsCancellationRequested ? ClientClosedRequest : StatusCodes.Status500InternalServerError;
            activity.Fail(e.GetType().Name);
            throw;
        }
        finally
        {
            if (activity is not null) Describe(activity, context, status ?? context.Response.StatusCode);
        }
    }

    private const int ClientClosedRequest = 499;

    private static void Describe(System.Diagnostics.Activity activity, HttpContext context, int status)
    {
        activity.SetTag("method", context.Request.Method);
        activity.SetTag("path", context.Request.Path.Value);
        activity.SetTag("route", (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText);
        activity.SetTag("status", status);
        if (context.User.Identity?.IsAuthenticated != true) return;
        activity.SetTag("user", context.User.UserId());
        activity.SetTag("device", context.User.DeviceId());
    }
}
