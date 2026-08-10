using System.Security.Claims;
using Microsoft.AspNetCore.Diagnostics;

namespace Clawbot.Api.Middleware;

/// <summary>
/// Converts unhandled exceptions into a safe JSON body and logs the full exception (Serilog → system_logs).
/// Does not expose stack traces, SQL, or filesystem paths to clients.
/// </summary>
public sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>499 Client Closed Request — không có trong StatusCodes nhưng là quy ước phổ biến.</summary>
    private const int ClientClosedRequestStatusCode = 499;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var requestId = httpContext.TraceIdentifier;

        // Khách tự hủy request (đóng tab, điều hướng, StrictMode double-fetch) thì RequestAborted đã
        // bị hủy. Ca này không phải lỗi hệ thống nhưng vẫn nổi lên đây dưới nhiều dạng khác nhau
        // (OperationCanceledException, RpcException "Cancelled", SqlException "Operation cancelled
        // by user"). Ghi ERR kèm stack cho chúng làm ngập log và che mất lỗi thật, nên hạ mức và
        // trả 499 thay vì 500. Không ghi body: đầu bên kia đã đi rồi.
        if (httpContext.RequestAborted.IsCancellationRequested)
        {
            LogClientAborted(
                logger,
                requestId,
                httpContext.Request.Method,
                httpContext.Request.Path.Value);
            if (!httpContext.Response.HasStarted)
                httpContext.Response.StatusCode = ClientClosedRequestStatusCode;
            return true;
        }

        var tenantId = httpContext.User?.FindFirstValue("tenant_id")
            ?? httpContext.User?.FindFirstValue("tid");
        var userId = httpContext.User?.FindFirstValue("sub")
            ?? httpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (exception is BadHttpRequestException badHttpRequestException)
        {
            // Minimal-API binding can include malformed submitted values in the exception message.
            // Log it internally but never return it to the client.
            LogInvalidRequest(
                logger,
                badHttpRequestException,
                requestId,
                httpContext.Request.Method,
                httpContext.Request.Path.Value,
                tenantId,
                userId);
            if (httpContext.Response.HasStarted)
                return false;

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsJsonAsync(
                new
                {
                    errorCode = "request.invalid_parameter",
                    message = "Thông tin gửi lên chưa hợp lệ.",
                    requestId,
                },
                cancellationToken).ConfigureAwait(false);
            return true;
        }

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
        EventId = 9000,
        Level = LogLevel.Information,
        Message = "Invalid request parameter requestId={RequestId} {Method} {Path} TenantId={TenantId} UserId={UserId}")]
    private static partial void LogInvalidRequest(
        ILogger logger,
        BadHttpRequestException ex,
        string requestId,
        string method,
        string? path,
        string? tenantId,
        string? userId);

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

    [LoggerMessage(
        EventId = 9002,
        Level = LogLevel.Information,
        Message = "Client aborted request requestId={RequestId} {Method} {Path}")]
    private static partial void LogClientAborted(
        ILogger logger,
        string requestId,
        string method,
        string? path);
}
