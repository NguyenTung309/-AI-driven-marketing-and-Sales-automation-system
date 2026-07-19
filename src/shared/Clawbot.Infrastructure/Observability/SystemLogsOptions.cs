namespace Clawbot.Infrastructure.Observability;

public sealed class SystemLogsOptions
{
    public const string SectionName = "SystemLogs";

    /// <summary>How long to keep system_logs rows. Default 30 days.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// When true, successful HTTP requests (2xx/3xx) are elevated to Warning so they enter the DB sink.
    /// Default false — only 4xx/5xx are persisted per request.
    /// </summary>
    public bool CaptureAllRequests { get; set; }
}

public sealed class AuditRetentionOptions
{
    public const string SectionName = "Audit";

    /// <summary>How long to keep audit_logs rows. Default 180 days.</summary>
    public int RetentionDays { get; set; } = 180;
}
