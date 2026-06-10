using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
        app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" })).AllowAnonymous();

        app.MapGet("/health/channels/pancake", async (
            AppDbContext db,
            CancellationToken ct) =>
        {
            var count = await db.PancakeConfigs
                .IgnoreQueryFilters()
                .CountAsync(ct);
            return Results.Ok(new
            {
                status = "ok",
                configured_tenants = count,
                adapter = "PancakeChannelAdapter",
            });
        }).AllowAnonymous();

        return app;
    }
}
