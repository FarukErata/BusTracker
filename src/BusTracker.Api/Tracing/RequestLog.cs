using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using BusTracker.Api.Configuration;
using BusTracker.Api.Data;
using Microsoft.Extensions.Options;

namespace BusTracker.Api.Tracing;

public sealed class RequestLog : BackgroundService
{
    private static readonly string[] Waits = ["ibb", "db", "wait"];
    private static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly TimeSpan ShownDbSpan = TimeSpan.FromMilliseconds(1);

    private readonly Channel<string> _lines = Channel.CreateBounded<string>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly RequestLogOptions _options;
    private readonly ILogger<RequestLog> _logger;
    private readonly ActivityListener? _listener;
    private long _pending;

    public RequestLog(IOptions<BusTrackerOptions> options, Database database, ILogger<RequestLog> logger)
    {
        _options = options.Value.RequestLog;
        _logger = logger;
        Folder = _options.Directory is { Length: > 0 } configured
            ? configured
            : Path.Combine(Path.GetDirectoryName(database.FilePath)!, "logs");
        if (!_options.Enabled) return;

        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Telemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnStopped,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public string Folder { get; }

    public async Task WhenWrittenAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (Interlocked.Read(ref _pending) > 0 && DateTime.UtcNow < until) await Task.Delay(20);
    }

    public override void Dispose()
    {
        _listener?.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        Directory.CreateDirectory(Folder);
        Prune();
        var pruned = DateTime.UtcNow.Date;
        try
        {
            while (await _lines.Reader.WaitToReadAsync(stoppingToken))
            {
                await WriteBatchAsync(stoppingToken);
                if (DateTime.UtcNow.Date == pruned) continue;
                pruned = DateTime.UtcNow.Date;
                Prune();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private void OnStopped(Activity activity)
    {
        var root = Telemetry.RootOf(activity);
        if (root == activity)
        {
            Enqueue(RootRecord(activity));
            return;
        }

        var span = ToSpan(activity);
        if (root is not null && !root.IsStopped)
        {
            ((ConcurrentQueue<SpanRecord>)root.GetCustomProperty(Telemetry.SpansProperty)!).Enqueue(span);
            return;
        }
        if (span.Name == "ibb") Enqueue(LateRecord(span, root));
    }

    private static SpanRecord ToSpan(Activity activity) =>
        new(activity.OperationName, activity.GetTagItem(Telemetry.DetailTag) as string, activity.StartTimeUtc, activity.Duration,
            activity.GetTagItem(Telemetry.ErrorTag) as string);

    private static JsonObject RootRecord(Activity root)
    {
        var kind = (string)root.GetCustomProperty(Telemetry.KindProperty)!;
        var spans = ((ConcurrentQueue<SpanRecord>)root.GetCustomProperty(Telemetry.SpansProperty)!).OrderBy(s => s.StartUtc).ToList();
        var record = new JsonObject
        {
            ["at"] = root.StartTimeUtc.ToString("O"),
            ["kind"] = kind,
            ["ms"] = Milliseconds(root.Duration),
        };
        if (kind == Telemetry.BackgroundKind) record["name"] = root.OperationName;
        AddTags(record, root);
        record["waits"] = Totals(spans);
        record["spans"] = new JsonArray(spans.Where(Shown).Select(s => SpanJson(s, root.StartTimeUtc)).ToArray<JsonNode?>());
        return record;
    }

    private static JsonObject LateRecord(SpanRecord span, Activity? root)
    {
        var record = new JsonObject
        {
            ["at"] = span.StartUtc.ToString("O"),
            ["kind"] = Telemetry.BackgroundKind,
            ["name"] = span.Name,
            ["ms"] = Milliseconds(span.Duration),
        };
        if (span.Detail is not null) record["detail"] = span.Detail;
        if (span.Error is not null) record["error"] = span.Error;
        if (root?.GetTagItem("path") is string path) record["startedBy"] = path;
        return record;
    }

    private static void AddTags(JsonObject record, Activity root)
    {
        foreach (var (key, value) in root.TagObjects)
        {
            record[key] = value switch
            {
                null => null,
                int i => i,
                long l => l,
                bool b => b,
                _ => value.ToString(),
            };
        }
    }

    private static JsonObject Totals(IReadOnlyList<SpanRecord> spans)
    {
        var totals = new JsonObject();
        foreach (var name in Waits)
        {
            var matching = spans.Where(s => s.Name == name).ToList();
            if (matching.Count == 0) continue;
            totals[name] = new JsonObject
            {
                ["count"] = matching.Count,
                ["ms"] = Milliseconds(TimeSpan.FromTicks(matching.Sum(s => s.Duration.Ticks))),
            };
        }
        return totals;
    }

    private static bool Shown(SpanRecord span) =>
        span.Name != "db" || span.Error is not null || span.Duration >= ShownDbSpan;

    private static JsonObject SpanJson(SpanRecord span, DateTime rootStart)
    {
        var json = new JsonObject
        {
            ["name"] = span.Name,
            ["at"] = Milliseconds(span.StartUtc - rootStart),
            ["ms"] = Milliseconds(span.Duration),
        };
        if (span.Detail is not null) json["detail"] = span.Detail;
        if (span.Error is not null) json["error"] = span.Error;
        return json;
    }

    private static double Milliseconds(TimeSpan duration) => Math.Round(duration.TotalMilliseconds, 1);

    private void Enqueue(JsonObject record)
    {
        Interlocked.Increment(ref _pending);
        if (!_lines.Writer.TryWrite(record.ToJsonString(Readable))) Interlocked.Decrement(ref _pending);
    }

    private async Task WriteBatchAsync(CancellationToken ct)
    {
        var batch = new List<string>();
        while (batch.Count < 500 && _lines.Reader.TryRead(out var line)) batch.Add(line);
        try
        {
            var file = Path.Combine(Folder, $"requests-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
            await File.AppendAllLinesAsync(file, batch, ct);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(e, "Could not write {Count} request log lines", batch.Count);
        }
        finally
        {
            Interlocked.Add(ref _pending, -batch.Count);
        }
    }

    private void Prune()
    {
        var oldest = DateTime.UtcNow.Date.AddDays(-_options.RetentionDays);
        foreach (var file in Directory.EnumerateFiles(Folder, "requests-*.jsonl"))
        {
            if (DateTime.TryParse(Path.GetFileNameWithoutExtension(file)["requests-".Length..], out var day) && day < oldest)
                File.Delete(file);
        }
    }
}
