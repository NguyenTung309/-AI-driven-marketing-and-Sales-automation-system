using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Clawbot.Agents.Core.Skills.Nlp;
using Microsoft.Data.SqlClient;
using Serilog.Core;
using Serilog.Events;

namespace Clawbot.Infrastructure.Observability;

/// <summary>
/// Serilog sink that batches Warning+ events into dbo.system_logs via SqlBulkCopy.
/// Never throws into the request path — failures go to SelfLog and the event is dropped.
/// </summary>
public sealed class SystemLogSink : ILogEventSink, IDisposable
{
    public const int MaxMessageLength = 2048;
    public const int DefaultBatchSize = 200;
    public static readonly TimeSpan DefaultFlushInterval = TimeSpan.FromSeconds(2);
    public const int DefaultQueueLimit = 10_000;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ReservedProps = new(StringComparer.OrdinalIgnoreCase)
    {
        "SourceContext", "RequestId", "RequestPath", "ConnectionId", "ActionId", "ActionName",
        "StatusCode", "Elapsed", "ElapsedMilliseconds", "RequestMethod", "Method", "Path",
        "TraceId", "TenantId", "UserId", "Application",
    };

    private readonly string _connectionString;
    private readonly string _source;
    private readonly IPiiRedactor _pii;
    private readonly int _batchSize;
    private readonly int _queueLimit;
    private readonly ConcurrentQueue<SystemLogRow> _queue = new();
    private readonly Timer _timer;
    private readonly object _flushLock = new();
    private int _queued;
    private bool _disposed;

    public SystemLogSink(
        string connectionString,
        string source,
        IPiiRedactor? pii = null,
        int batchSize = DefaultBatchSize,
        TimeSpan? flushInterval = null,
        int queueLimit = DefaultQueueLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        _connectionString = connectionString;
        _source = source;
        _pii = pii ?? new RegexPiiRedactor();
        _batchSize = Math.Clamp(batchSize, 1, 2000);
        _queueLimit = Math.Max(100, queueLimit);
        // Drain aggressively each tick so burst traffic does not stall at ~1 batch / 2s.
        _timer = new Timer(
            _ => SafeFlush(drainAll: true),
            null,
            flushInterval ?? DefaultFlushInterval,
            flushInterval ?? DefaultFlushInterval);
    }

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        if (_disposed) return;
        if (logEvent.Level < LogEventLevel.Warning) return;

        if (Volatile.Read(ref _queued) >= _queueLimit)
            return;

        try
        {
            var row = Map(logEvent);
            _queue.Enqueue(row);
            // Never SqlBulkCopy on the request/logging thread — timer drains the queue.
            if (Interlocked.Increment(ref _queued) >= _batchSize)
                ThreadPool.QueueUserWorkItem(static state => ((SystemLogSink)state!).SafeFlush(), this);
        }
        catch (Exception ex)
        {
            SelfLog($"SystemLogSink.Emit failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        // Drain remaining queue (multiple batches if needed) before process exit.
        SafeFlush(drainAll: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Visible for unit tests — maps a Serilog event to a row without DB I/O.</summary>
    internal SystemLogRow MapForTests(LogEvent logEvent) => Map(logEvent);

    private SystemLogRow Map(LogEvent logEvent)
    {
        // Redact first so truncation cannot split a phone/email and let PII through the regex.
        var message = Truncate(Redact(RenderMessage(logEvent)), MaxMessageLength) ?? string.Empty;

        var exception = logEvent.Exception?.ToString();
        if (exception is { Length: > 0 })
            exception = Truncate(exception, 32_000);

        var category = GetString(logEvent, "SourceContext");
        var statusCode = GetInt(logEvent, "StatusCode");
        var method = GetString(logEvent, "RequestMethod") ?? GetString(logEvent, "Method");
        var path = GetString(logEvent, "RequestPath") ?? GetString(logEvent, "Path");
        var elapsed = GetDouble(logEvent, "Elapsed") ?? GetDouble(logEvent, "ElapsedMilliseconds");
        var traceId = GetString(logEvent, "TraceId") ?? GetString(logEvent, "RequestId");
        var tenantId = GetGuid(logEvent, "TenantId");
        var userId = GetGuid(logEvent, "UserId");
        var source = GetString(logEvent, "Application") ?? _source;
        var properties = BuildPropertiesJson(logEvent);

        return new SystemLogRow(
            OccurredAt: logEvent.Timestamp.ToUniversalTime(),
            Level: LevelName(logEvent.Level),
            Source: Truncate(source, 32)!,
            Category: Truncate(category, 256),
            Message: message,
            Exception: exception,
            StatusCode: statusCode,
            Method: Truncate(method, 10),
            Path: Truncate(path, 512),
            ElapsedMs: elapsed,
            TraceId: Truncate(traceId, 64),
            TenantId: tenantId,
            UserId: userId,
            Properties: properties);
    }

    private void SafeFlush(bool drainAll = false)
    {
        if (!Monitor.TryEnter(_flushLock)) return;
        try
        {
            do
            {
                if (!FlushCore()) break;
            }
            while (drainAll && Volatile.Read(ref _queued) > 0);
        }
        catch (Exception ex)
        {
            SelfLog($"SystemLogSink.Flush failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Monitor.Exit(_flushLock);
        }
    }

    /// <returns>true if a non-empty batch was written.</returns>
    private bool FlushCore()
    {
        var batch = new List<SystemLogRow>(_batchSize);
        while (batch.Count < _batchSize && _queue.TryDequeue(out var row))
        {
            Interlocked.Decrement(ref _queued);
            batch.Add(row);
        }

        if (batch.Count == 0) return false;

        using var table = BuildDataTable(batch);
        using var conn = new SqlConnection(_connectionString);
        conn.Open();
        using var bulk = new SqlBulkCopy(conn)
        {
            DestinationTableName = "dbo.system_logs",
            BulkCopyTimeout = 30,
            BatchSize = batch.Count,
        };
        foreach (DataColumn col in table.Columns)
            bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
        bulk.WriteToServer(table);
        return true;
    }

    private static DataTable BuildDataTable(IReadOnlyList<SystemLogRow> rows)
    {
        var table = new DataTable();
        table.Columns.Add("occurred_at", typeof(DateTimeOffset));
        table.Columns.Add("level", typeof(string));
        table.Columns.Add("source", typeof(string));
        table.Columns.Add("category", typeof(string));
        table.Columns.Add("message", typeof(string));
        table.Columns.Add("exception", typeof(string));
        table.Columns.Add("status_code", typeof(int));
        table.Columns.Add("method", typeof(string));
        table.Columns.Add("path", typeof(string));
        table.Columns.Add("elapsed_ms", typeof(double));
        table.Columns.Add("trace_id", typeof(string));
        table.Columns.Add("tenant_id", typeof(Guid));
        table.Columns.Add("user_id", typeof(Guid));
        table.Columns.Add("properties", typeof(string));

        foreach (var r in rows)
        {
            table.Rows.Add(
                r.OccurredAt,
                r.Level,
                r.Source,
                (object?)r.Category ?? DBNull.Value,
                r.Message,
                (object?)r.Exception ?? DBNull.Value,
                (object?)r.StatusCode ?? DBNull.Value,
                (object?)r.Method ?? DBNull.Value,
                (object?)r.Path ?? DBNull.Value,
                (object?)r.ElapsedMs ?? DBNull.Value,
                (object?)r.TraceId ?? DBNull.Value,
                (object?)r.TenantId ?? DBNull.Value,
                (object?)r.UserId ?? DBNull.Value,
                (object?)r.Properties ?? DBNull.Value);
        }

        return table;
    }

    private string Redact(string text)
    {
        try
        {
            return _pii.RedactAsync(text, CancellationToken.None).GetAwaiter().GetResult().RedactedText;
        }
        catch
        {
            return text;
        }
    }

    private static string RenderMessage(LogEvent logEvent)
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb, CultureInfo.InvariantCulture);
        logEvent.RenderMessage(writer, CultureInfo.InvariantCulture);
        return sb.ToString();
    }

    private static string? BuildPropertiesJson(LogEvent logEvent)
    {
        Dictionary<string, object?>? bag = null;
        foreach (var (key, value) in logEvent.Properties)
        {
            if (ReservedProps.Contains(key)) continue;
            bag ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            bag[key] = Simplify(value);
        }

        return bag is null || bag.Count == 0
            ? null
            : Truncate(JsonSerializer.Serialize(bag, JsonOpts), 8000);
    }

    private static object? Simplify(LogEventPropertyValue value) => value switch
    {
        ScalarValue s => s.Value is IFormattable f
            ? f.ToString(null, CultureInfo.InvariantCulture)
            : s.Value?.ToString(),
        SequenceValue seq => seq.Elements.Select(Simplify).ToArray(),
        StructureValue st => st.Properties.ToDictionary(p => p.Name, p => Simplify(p.Value), StringComparer.Ordinal),
        DictionaryValue dict => dict.Elements.ToDictionary(
            kv => kv.Key.Value?.ToString() ?? string.Empty,
            kv => Simplify(kv.Value),
            StringComparer.Ordinal),
        _ => value.ToString(),
    };

    private static string LevelName(LogEventLevel level) => level switch
    {
        LogEventLevel.Warning => "Warning",
        LogEventLevel.Error => "Error",
        LogEventLevel.Fatal => "Fatal",
        _ => level.ToString(),
    };

    private static string? GetString(LogEvent e, string name)
    {
        if (!e.Properties.TryGetValue(name, out var v)) return null;
        return v switch
        {
            ScalarValue { Value: string s } => s,
            ScalarValue { Value: { } o } => Convert.ToString(o, CultureInfo.InvariantCulture),
            _ => v.ToString()?.Trim('"'),
        };
    }

    private static int? GetInt(LogEvent e, string name)
    {
        if (!e.Properties.TryGetValue(name, out var v) || v is not ScalarValue s || s.Value is null)
            return null;
        try
        {
            return Convert.ToInt32(s.Value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static double? GetDouble(LogEvent e, string name)
    {
        if (!e.Properties.TryGetValue(name, out var v) || v is not ScalarValue s || s.Value is null)
            return null;
        try
        {
            return Convert.ToDouble(s.Value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static Guid? GetGuid(LogEvent e, string name)
    {
        var raw = GetString(e, name);
        return Guid.TryParse(raw, out var g) && g != Guid.Empty ? g : null;
    }

    private static string? Truncate(string? value, int max)
    {
        if (value is null) return null;
        return value.Length <= max ? value : value[..max];
    }

    private static void SelfLog(string message)
    {
        try
        {
            Serilog.Debugging.SelfLog.WriteLine(message);
        }
        catch
        {
            // last resort: ignore
        }
    }

    internal sealed record SystemLogRow(
        DateTimeOffset OccurredAt,
        string Level,
        string Source,
        string? Category,
        string Message,
        string? Exception,
        int? StatusCode,
        string? Method,
        string? Path,
        double? ElapsedMs,
        string? TraceId,
        Guid? TenantId,
        Guid? UserId,
        string? Properties);
}
