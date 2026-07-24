using System.Security.Claims;
using Microsoft.AspNetCore.Diagnostics;

namespace Clawbot.Api.Middleware;

/// <summary>
/// Converts unhandled exceptions into a safe JSON body and logs the full exception (Serilog → system_logs).
/// Does not expose stack traces, SQL, or filesystem paths to clients.
/// </summary>
public sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var requestId = httpContext.TraceIdentifier;
        var tenantId = httpContext.User?.FindFirstValue("tenant_id")
            ?? httpContext.User?.FindFirstValue("tid");
        var userId = httpContext.User?.FindFirstValue("sub")
            ?? httpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        // Structured props survive LogContext pop when exception unwinds past enrichment middleware.
        LogUnhandled(
            logger,
            exception,
            requestId,
            httpContext.Request.Method,
            httpContext.Request.Path.Value,
            tenantId,
            userId);

        if (httpContext.Response.HasStarted)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                errorCode = "internal_error",
                message = "Đã xảy ra lỗi hệ thống, vui lòng thử lại.",
                requestId,
            },
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    [LoggerMessage(
        EventId = 9001,
        Level = LogLevel.Error,
        Message = "Unhandled exception requestId={RequestId} {Method} {Path} TenantId={TenantId} UserId={UserId}")]
    private static partial void LogUnhandled(
        ILogger logger,
        Exception ex,
        string requestId,
        string method,
        string? path,
        string? tenantId,
        string? userId);
}
