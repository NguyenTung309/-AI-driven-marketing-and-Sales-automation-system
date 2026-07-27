namespace Clawbot.Domain.Observability;

/// <summary>
/// Read model for dbo.system_logs. Rows are written by <c>SystemLogSink</c> (SqlBulkCopy), not EF.
/// </summary>
public sealed class SystemLogEntry
{
    public long Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string Level { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
    public string? Category { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public string? Exception { get; private set; }
    public int? StatusCode { get; private set; }
    public string? Method { get; private set; }
    public string? Path { get; private set; }
    public double? ElapsedMs { get; private set; }
    public string? TraceId { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Properties { get; private set; }

    private SystemLogEntry() { }
}
