using System.Text.Json;
using Clawbot.SharedKernel.Demo;

namespace Clawbot.Api.Middleware;

public sealed class DemoModeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DemoOptions _opts;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public DemoModeMiddleware(RequestDelegate next, IConfiguration cfg)
    {
        _next = next;
        _opts = cfg.GetSection(DemoOptions.Section).Get<DemoOptions>() ?? new DemoOptions();
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        // Block demo endpoints if not in demo mode
        if (ctx.Request.Path.StartsWithSegments("/api/demo"))
        {
            if (!_opts.Mode)
            {
                ctx.Response.StatusCode = 404;
                return;
            }

            // Admin key check for sensitive endpoints
            var sensitive = ctx.Request.Path.StartsWithSegments("/api/demo/config")
                         || ctx.Request.Path.StartsWithSegments("/api/demo/traces")
                         || ctx.Request.Path.StartsWithSegments("/api/demo/events");
            if (sensitive)
            {
                if (string.IsNullOrEmpty(_opts.AdminKey))
                {
                    ctx.Response.StatusCode = 503;
                    await ctx.Response.WriteAsJsonAsync(new { error = "demo_admin_key_not_configured" }, JsonOpts);
                    return;
                }

                var auth = ctx.Request.Headers.Authorization.ToString();
                if (!auth.Equals($"Bearer {_opts.AdminKey}", StringComparison.Ordinal))
                {
                    ctx.Response.StatusCode = 401;
                    await ctx.Response.WriteAsJsonAsync(new { error = "unauthorized" }, JsonOpts);
                    return;
                }
            }

            // Check internal network (loopback or private range)
            var ip = ctx.Connection.RemoteIpAddress;
            if (ip is not null && !IsPrivateIp(ip))
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsJsonAsync(new { error = "demo_only_internal_network" }, JsonOpts);
                return;
            }
        }

        await _next(ctx);
    }

    private static bool IsPrivateIp(System.Net.IPAddress ip)
    {
        if (System.Net.IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        if (b.Length == 16)
            return ip.IsIPv6LinkLocal || (b[0] & 0xfe) == 0xfc; // fe80::/10 or fc00::/7
        return b[0] == 10
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168);
    }
}
