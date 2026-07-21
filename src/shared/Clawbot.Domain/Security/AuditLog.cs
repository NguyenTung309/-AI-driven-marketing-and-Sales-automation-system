using System.Net;
using Clawbot.Domain.Common;

namespace Clawbot.Domain.Security;

public sealed class AuditLog : Entity<Guid>, ITenantOwned
{
    private const int MaxEventKeyLength = 256;

    public Guid TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public Guid? ResourceId { get; private set; }
    public string? DiffJson { get; private set; }
    public IPAddress? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? EventKey { get; private set; }
    public long? StateSequence { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(
        Guid tenantId,
        Guid? userId,
        string action,
        string resourceType,
        Guid? resourceId,
        DateTimeOffset occurredAt,
        string? diffJson = null,
        IPAddress? ip = null,
        string? userAgent = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            DiffJson = diffJson,
            IpAddress = ip,
            UserAgent = userAgent,
            OccurredAt = occurredAt,
        };

    public static AuditLog CreateBusinessEvent(
        Guid tenantId,
        Guid? userId,
        string action,
        string resourceType,
        Guid? resourceId,
        DateTimeOffset occurredAt,
        string eventKey,
        long stateSequence,
        string? diffJson = null)
    {
        if (string.IsNullOrWhiteSpace(eventKey))
            throw new ArgumentException("audit_event_key_required", nameof(eventKey));
        var normalizedEventKey = eventKey.Trim();
        if (normalizedEventKey.Length > MaxEventKeyLength)
            throw new ArgumentException("audit_event_key_too_long", nameof(eventKey));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stateSequence);

        var audit = Create(
            tenantId,
            userId,
            action,
            resourceType,
            resourceId,
            occurredAt,
            diffJson);
        audit.EventKey = normalizedEventKey;
        audit.StateSequence = stateSequence;
        return audit;
    }
}
