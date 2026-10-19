using System.Collections.Concurrent;
using System.Diagnostics;

namespace BusTracker.Api.Tracing;

public sealed record SpanRecord(string Name, string? Detail, DateTime StartUtc, TimeSpan Duration, string? Error);

public static class Telemetry
{
    public const string SourceName = "BusTracker";
    public const string RequestKind = "request";
    public const string BackgroundKind = "background";

    internal const string DetailTag = "detail";
    internal const string ErrorTag = "error";
    internal const string KindProperty = "trace.kind";
    internal const string SpansProperty = "trace.spans";

    private static readonly ActivitySource Source = new(SourceName);

    public static bool Recording => RootOf(Activity.Current) is not null;

    public static Activity? Span(string name, string? detail = null)
    {
        if (!Recording) return null;
        var activity = Source.StartActivity(name);
        if (detail is not null) activity?.SetTag(DetailTag, detail);
        return activity;
    }

    public static Activity? Request(string name) => Root(name, RequestKind, null);

    public static Activity? Background(string name, string? detail = null)
    {
        Activity.Current = null;
        return Root(name, BackgroundKind, detail);
    }

    public static void Fail(this Activity? activity, string reason) => activity?.SetTag(ErrorTag, reason);

    public static async Task EnterAsync(SemaphoreSlim gate, string detail, CancellationToken ct)
    {
        if (gate.Wait(0)) return;
        using var span = Span("wait", detail);
        await gate.WaitAsync(ct);
    }

    public static void Note(string key, object? value) => RootOf(Activity.Current)?.SetTag(key, value);

    internal static Activity? RootOf(Activity? activity)
    {
        for (var current = activity; current is not null; current = current.Parent)
            if (current.GetCustomProperty(KindProperty) is not null) return current;
        return null;
    }

    private static Activity? Root(string name, string kind, string? detail)
    {
        var activity = Source.StartActivity(name);
        if (activity is null) return null;
        activity.SetCustomProperty(KindProperty, kind);
        activity.SetCustomProperty(SpansProperty, new ConcurrentQueue<SpanRecord>());
        if (detail is not null) activity.SetTag(DetailTag, detail);
        return activity;
    }
}
