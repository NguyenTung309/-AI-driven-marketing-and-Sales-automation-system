using Serilog.Context;

namespace Clawbot.Gateway.Middleware;

/// <summary>
/// Middleware that ensures every request has an X-Trace-Id header for distributed tracing.
/// If not present, generates a new one.
/// </summary>
public class TraceIdMiddleware
{
    private readonly RequestDelegate _next;

    public TraceIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // EARS[WHEN any request arrives THE SYSTEM SHALL ensure X-Trace-Id is present and propagated]
        var traceId = context.Request.Headers["X-Trace-Id"].FirstOrDefault()
                      ?? Guid.NewGuid().ToString("N");

        context.Request.Headers["X-Trace-Id"] = traceId;
        context.Response.Headers["X-Trace-Id"] = traceId;

        using (LogContext.PushProperty("TraceId", traceId))
        {
            await _next(context);
        }
    }
}
