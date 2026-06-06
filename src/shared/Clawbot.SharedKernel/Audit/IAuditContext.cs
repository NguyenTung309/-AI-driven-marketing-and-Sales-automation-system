using System.Net;

namespace Clawbot.SharedKernel.Audit;

public interface IAuditContext
{
    Guid? UserId { get; }
    IPAddress? IpAddress { get; }
    string? UserAgent { get; }
}

public sealed class StaticAuditContext(Guid? userId = null, IPAddress? ipAddress = null, string? userAgent = null) : IAuditContext
{
    public Guid? UserId { get; } = userId;
    public IPAddress? IpAddress { get; } = ipAddress;
    public string? UserAgent { get; } = userAgent;
}
