using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;
using Clawbot.Gateway.Configuration;
using Serilog;

namespace Clawbot.Gateway.Middleware;

/// <summary>
/// Middleware that validates Pancake webhook HMAC-SHA256 signature.
/// Hard rule: HMAC must be valid before payload is deserialized by any downstream handler.
/// </summary>
public class PancakeHmacMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PancakeHmacMiddleware> _logger;
    private readonly IOptionsMonitor<GatewayOptions> _gatewayOptions;

    public PancakeHmacMiddleware(
        RequestDelegate next,
        ILogger<PancakeHmacMiddleware> logger,
        IOptionsMonitor<GatewayOptions> gatewayOptions)
    {
        _next = next;
        _logger = logger;
        _gatewayOptions = gatewayOptions;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Check if this route requires HMAC validation
        var endpoint = context.GetEndpoint();
        var requiresHmac = endpoint?.Metadata.GetMetadata<bool>("RequiresHmac") ?? false;

        if (!requiresHmac)
        {
            await _next(context);
            return;
        }

        // EARS[WHEN webhook request arrives THE SYSTEM SHALL validate HMAC-SHA256 signature before processing]
        
        // Read raw body for signature validation
        context.Request.EnableBuffering();
        var bodyBytes = await ReadRequestBodyAsync(context.Request);
        context.Request.Body.Position = 0;

        // Extract signature from header
        var signatureHeader = context.Request.Headers["X-Pancake-Signature"].FirstOrDefault()
                            ?? context.Request.Headers["X-Hub-Signature-256"].FirstOrDefault();

        if (string.IsNullOrEmpty(signatureHeader))
        {
            _logger.LogWarning("HMAC validation failed: missing signature header. TraceId={TraceId}",
                context.Request.Headers["X-Trace-Id"].FirstOrDefault());
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: missing signature");
            return;
        }

        // Compute expected signature
        var secret = _gatewayOptions.CurrentValue.Pancake.WebhookSecret;
        var expectedSignature = ComputeHmac(secret, bodyBytes);

        // Constant-time comparison (prevent timing attacks)
        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedSignature),
            Encoding.ASCII.GetBytes(signatureHeader)))
        {
            _logger.LogWarning("HMAC validation failed: signature mismatch. TraceId={TraceId}",
                context.Request.Headers["X-Trace-Id"].FirstOrDefault());
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: invalid signature");
            return;
        }

        // HMAC is valid, proceed
        await _next(context);
    }

    private static async Task<byte[]> ReadRequestBodyAsync(HttpRequest request)
    {
        using var memoryStream = new MemoryStream();
        await request.Body.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }

    private static string ComputeHmac(string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hashBytes = hmac.ComputeHash(body);
        return Convert.ToHexString(hashBytes).ToLower();
    }
}
