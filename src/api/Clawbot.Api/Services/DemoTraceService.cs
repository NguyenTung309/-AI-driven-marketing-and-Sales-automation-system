using System.Runtime.CompilerServices;
using System.Text.Json;
using Clawbot.SharedKernel.Demo;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Clawbot.Api.Services;

public sealed class DemoTraceService
{
    private readonly IDatabase _redis;
    private readonly IOptions<DemoOptions> _options;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private const string TraceKeyPrefix = "demo:trace:";
    private const string TraceIndexKey = "demo:traces:index";
    private const string EventIdKey = "demo:event:id";
    private const string MaxTraceIndex = "demo:traces:max";

    public DemoTraceService(IConnectionMultiplexer mux, IOptions<DemoOptions> options)
    {
        _redis = mux.GetDatabase();
        _options = options;
    }

    public async Task<string> CreateTraceAsync(string? existingTraceId = null)
    {
        var traceId = existingTraceId ?? $"trc_{Guid.NewGuid():N}"[..20];
        var trace = new DemoTrace
        {
            TraceId = traceId,
            Status = DemoTraceStatus.Running,
            CreatedAtUtc = DateTime.UtcNow,
        };
        await SaveTraceAsync(trace);
        await AppendToIndexAsync(traceId);
        return traceId;
    }

    public async Task AppendStepAsync(string traceId, DemoTraceStep step)
    {
        var trace = await GetTraceAsync(traceId);
        if (trace is null) return;

        step.TimestampUtc ??= DateTime.UtcNow;
        trace.AddStep(step);

        if (step.Status is DemoTraceStepStatus.Failed or DemoTraceStepStatus.Skipped)
        {
            if (!string.IsNullOrEmpty(step.Reason))
                trace.Errors.Add($"[{step.Layer}] {step.Reason}");
        }

        await SaveTraceAsync(trace);
        await PublishSseEventAsync(traceId, step);
    }

    public async Task CompleteTraceAsync(string traceId)
    {
        var trace = await GetTraceAsync(traceId);
        if (trace is null) return;

        trace.Status = trace.Errors.Count > 0 ? DemoTraceStatus.Partial : DemoTraceStatus.Completed;
        trace.CompletedAtUtc = DateTime.UtcNow;
        trace.TotalDurationMs = (long)(trace.CompletedAtUtc.Value - trace.CreatedAtUtc).TotalMilliseconds;

        // Close any step still running
        foreach (var s in trace.Steps)
        {
            if (s.Status is DemoTraceStepStatus.Pending or DemoTraceStepStatus.Running)
            {
                s.Status = s.Layer == "outbound" && trace.Errors.Any(e => e.Contains("token"))
                    ? DemoTraceStepStatus.Skipped
                    : DemoTraceStepStatus.Failed;
                s.DurationMs ??= 0;
            }
        }

        await SaveTraceAsync(trace);
        await PublishCompleteEventAsync(trace);
    }

    public async Task<DemoTrace?> GetTraceAsync(string traceId)
    {
        var data = await _redis.StringGetAsync($"{TraceKeyPrefix}{traceId}");
        return data.HasValue ? JsonSerializer.Deserialize<DemoTrace>((string)data!, JsonOpts) : null;
    }

    public async Task<List<DemoTrace>> GetRecentTracesAsync(int count = 10)
    {
        var ids = await _redis.ListRangeAsync(TraceIndexKey, 0, count - 1);
        var traces = new List<DemoTrace>();
        foreach (var id in ids)
        {
            var t = await GetTraceAsync(id!);
            if (t is not null) traces.Add(t);
        }
        return traces;
    }

    public async Task<string> GetTraceExportJsonAsync(string traceId)
    {
        var trace = await GetTraceAsync(traceId);
        return trace is null ? "null" : JsonSerializer.Serialize(trace, JsonOpts);
    }

    /// <summary>Mark a trace as abandoned (watchdog).</summary>
    public async Task AbandonTraceAsync(string traceId, string reason)
    {
        var trace = await GetTraceAsync(traceId);
        if (trace is null || trace.Status != DemoTraceStatus.Running) return;

        trace.Status = DemoTraceStatus.Partial;
        trace.CompletedAtUtc = DateTime.UtcNow;
        trace.AddStep(new DemoTraceStep
        {
            Layer = "watchdog",
            Status = DemoTraceStepStatus.Failed,
            Reason = reason,
            TimestampUtc = DateTime.UtcNow,
        });
        trace.Errors.Add($"[watchdog] {reason}");
        await SaveTraceAsync(trace);
        await PublishCompleteEventAsync(trace);
    }

    /// <summary>Mark outbox as timed out (watchdog).</summary>
    public async Task FailOutboxStepAsync(string traceId)
    {
        var trace = await GetTraceAsync(traceId);
        if (trace is null) return;

        var outbox = trace.Steps.Find(s => s.Layer == "outbox");
        if (outbox is not null && outbox.Status is DemoTraceStepStatus.Pending or DemoTraceStepStatus.Running)
        {
            outbox.Status = DemoTraceStepStatus.Failed;
            outbox.Reason = "rabbit_publish_timeout";
            trace.Status = DemoTraceStatus.Partial;
            trace.Errors.Add("[outbox] rabbit_publish_timeout");
        }
        await SaveTraceAsync(trace);
    }

    /// <summary>SSE: subscribe to live events.</summary>
    public async IAsyncEnumerable<string> SubscribeEventsAsync(string? lastEventId, int replayCount, [EnumeratorCancellation] CancellationToken ct)
    {
        var startId = ParseLastEventId(lastEventId);

        // Replay recent traces if no Last-Event-ID
        if (startId is null && replayCount > 0)
        {
            var recent = await GetRecentTracesAsync(replayCount);
            foreach (var tr in recent)
            {
                if (ct.IsCancellationRequested) yield break;
                foreach (var step in tr.Steps)
                {
                    yield return FormatSseEvent("trace_step", step, tr.TraceId);
                }
                yield return FormatSseEvent("trace_complete", new { tr.Status, tr.TraceId, tr.TotalDurationMs }, tr.TraceId);
            }
            yield return "event: replay_done\ndata: {}\n\n";
        }

        // Live stream: poll the index for new events — simplified approach
        var lastSeen = startId ?? DateTime.UtcNow.Ticks;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(500, ct);
            var events = await GetNewEventsAsync(lastSeen);
            foreach (var e in events)
            {
                yield return e;
                lastSeen = DateTime.UtcNow.Ticks;
            }
        }
    }

    // //////////////////////////////////////////////////////////////////
    // Private helpers
    // //////////////////////////////////////////////////////////////////

    private async Task SaveTraceAsync(DemoTrace trace)
    {
        var ttl = _options.Value.EffectiveTtlMinutes;
        var key = $"{TraceKeyPrefix}{trace.TraceId}";
        var json = JsonSerializer.Serialize(trace, JsonOpts);
        await _redis.StringSetAsync(key, json, TimeSpan.FromMinutes(ttl));
    }

    private async Task AppendToIndexAsync(string traceId)
    {
        await _redis.ListLeftPushAsync(TraceIndexKey, traceId);
        await _redis.ListTrimAsync(TraceIndexKey, 0, 99); // keep last 100
        await _redis.KeyExpireAsync(TraceIndexKey, TimeSpan.FromHours(2));
    }

    private async Task<long> NextEventIdAsync()
    {
        return await _redis.StringIncrementAsync(EventIdKey);
    }

    private async Task PublishSseEventAsync(string traceId, DemoTraceStep step)
    {
        var eid = await NextEventIdAsync();
        var key = $"demo:events:{eid}";
        var payload = JsonSerializer.Serialize(new { traceId, step.Layer, status = step.Status.ToString(), step.DurationMs }, JsonOpts);
        await _redis.StringSetAsync(key, payload, TimeSpan.FromMinutes(10));
        await _redis.PublishAsync(RedisChannel.Literal("demo:events"), $"{eid}|trace_step|{payload}");
    }

    private async Task PublishCompleteEventAsync(DemoTrace trace)
    {
        var eid = await NextEventIdAsync();
        var key = $"demo:events:{eid}";
        var payload = JsonSerializer.Serialize(new { trace.TraceId, trace.Status, trace.TotalDurationMs }, JsonOpts);
        await _redis.StringSetAsync(key, payload, TimeSpan.FromMinutes(10));
        await _redis.PublishAsync(RedisChannel.Literal("demo:events"), $"{eid}|trace_complete|{payload}");
    }

    private static Task<List<string>> GetNewEventsAsync(long sinceTicks)
    {
        // Simplified: read from published events via pub/sub (real impl would use dedicated channel)
        return Task.FromResult(new List<string>()); // SSE streaming with pub/sub channel in real impl
    }

    private static long? ParseLastEventId(string? lastEventId)
    {
        if (string.IsNullOrEmpty(lastEventId)) return null;
        return long.TryParse(lastEventId, out var id) ? id : null;
    }

    private static string FormatSseEvent(string eventType, object data, string traceId)
    {
        var json = JsonSerializer.Serialize(data, JsonOpts);
        return $"event: {eventType}\nid: {traceId}\ndata: {json}\n\n";
    }
}
