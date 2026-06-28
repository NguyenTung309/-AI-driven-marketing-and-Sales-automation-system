using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Clawbot.Api.Middleware;

public static class RateLimitingExtensions
{
    public const string AuthPolicy = "auth";
    public const string WebhookPolicy = "webhook";
    public const string ChatPolicy = "chat";
    public const string GeneralPolicy = "general";
    public const string UploadPolicy = "upload";

    public static IServiceCollection AddClawbotRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(AuthPolicy, ctx => PartitionForRemoteIp(ctx, permit: 10, window: TimeSpan.FromMinutes(1)));
            options.AddPolicy(WebhookPolicy, ctx => PartitionForRemoteIp(ctx, permit: 120, window: TimeSpan.FromMinutes(1)));
            options.AddPolicy(ChatPolicy, ctx => PartitionForUserOrIp(ctx, permit: 60, window: TimeSpan.FromMinutes(1)));
            options.AddPolicy(GeneralPolicy, ctx => PartitionForUserOrIp(ctx, permit: 300, window: TimeSpan.FromMinutes(1)));
            // File parsing (OpenXml/PdfPig) is CPU+memory heavy — keep it well below the general limit.
            options.AddPolicy(UploadPolicy, ctx => PartitionForUserOrIp(ctx, permit: 20, window: TimeSpan.FromMinutes(1)));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 600,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }

    private static RateLimitPartition<string> PartitionForRemoteIp(HttpContext ctx, int permit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permit,
                Window = window,
                QueueLimit = 0,
            });

    private static RateLimitPartition<string> PartitionForUserOrIp(HttpContext ctx, int permit, TimeSpan window)
    {
        var user = ctx.User?.FindFirst("sub")?.Value
            ?? ctx.User?.FindFirst("tenant_id")?.Value
            ?? ctx.Connection.RemoteIpAddress?.ToString()
            ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: user,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permit,
                Window = window,
                QueueLimit = 0,
            });
    }

    public static string PolicyHeader(string policy, int permit) =>
        string.Create(CultureInfo.InvariantCulture, $"{policy}:{permit}/min");
}
