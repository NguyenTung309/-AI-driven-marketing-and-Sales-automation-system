using System.Net;
using Clawbot.Domain.Common;

namespace Clawbot.Domain.Security;

public sealed class AuditLog : Entity<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public Guid? ResourceId { get; private set; }
    public string? DiffJson { get; private set; }
    public IPAddress? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
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
}
