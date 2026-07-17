using System.Net;
using System.Security.Claims;
using Clawbot.SharedKernel.Audit;
using Microsoft.AspNetCore.Http;

namespace Clawbot.Infrastructure.Audit;

public sealed class HttpAuditContext(IHttpContextAccessor accessor) : IAuditContext
{
    public const int MaxUserAgentLength = 512;

    private readonly IHttpContextAccessor _accessor = accessor;

    public Guid? UserId
    {
        get
        {
            var sub = _accessor.HttpContext?.User?.FindFirstValue("sub");
            return Guid.TryParse(sub, out var g) ? g : null;
        }
    }

    public IPAddress? IpAddress => _accessor.HttpContext?.Connection?.RemoteIpAddress;

    public string? UserAgent
    {
        get
        {
            var value = _accessor.HttpContext?.Request?.Headers.UserAgent.ToString();
            if (string.IsNullOrEmpty(value)) return null;
            return value.Length <= MaxUserAgentLength ? value : value[..MaxUserAgentLength];
        }
    }
}
