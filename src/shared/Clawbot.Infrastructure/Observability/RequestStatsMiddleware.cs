using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Infrastructure.Observability;

/// <summary>
/// After the response completes, increments in-memory request stats (2xx/4xx/5xx).
/// Must run after authentication so tenant claims are available.
/// </summary>
public sealed class RequestStatsMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context, RequestStatsCounter counter)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(counter);

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                var tenantRaw = context.User?.FindFirstValue("tenant_id")
                    ?? context.User?.FindFirstValue("tid");
                Guid? tenantId = Guid.TryParse(tenantRaw, out var g) && g != Guid.Empty ? g : null;
                counter.Increment(tenantId, context.Response.StatusCode, DateTimeOffset.UtcNow);
            }
            catch
            {
                // never fail the request
            }
        }
    }
}
