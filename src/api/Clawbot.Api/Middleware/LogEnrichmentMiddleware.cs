using System.Security.Claims;
using Serilog.Context;

namespace Clawbot.Api.Middleware;

/// <summary>
/// Pushes TraceId / TenantId / UserId into Serilog LogContext so request logs and exceptions
/// land in system_logs with the same requestId the client receives.
/// Must run after authentication.
/// </summary>
public sealed class LogEnrichmentMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tenantId = context.User?.FindFirstValue("tenant_id")
            ?? context.User?.FindFirstValue("tid");
        var userId = context.User?.FindFirstValue("sub")
            ?? context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        using (LogContext.PushProperty("TraceId", context.TraceIdentifier))
        using (LogContext.PushProperty("TenantId", tenantId ?? string.Empty))
        using (LogContext.PushProperty("UserId", userId ?? string.Empty))
        {
            await _next(context).ConfigureAwait(false);
        }
    }
}
