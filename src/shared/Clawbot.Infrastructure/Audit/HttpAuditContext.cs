using System.Net;
using System.Security.Claims;
using Clawbot.SharedKernel.Audit;
using Microsoft.AspNetCore.Http;

namespace Clawbot.Infrastructure.Audit;

public sealed class HttpAuditContext(IHttpContextAccessor accessor) : IAuditContext
{
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
            var v = _accessor.HttpContext?.Request?.Headers["User-Agent"].ToString();
            return string.IsNullOrEmpty(v) ? null : v;
        }
    }
}
