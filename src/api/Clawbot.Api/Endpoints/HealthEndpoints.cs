using Clawbot.Api.Services;

namespace Clawbot.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
        app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" })).AllowAnonymous();

        app.MapGet("/health/channels/pancake", async (
            ChannelHealthService health,
            CancellationToken ct) => Results.Ok(await health.GetPancakeAsync(ct).ConfigureAwait(false))).AllowAnonymous();

        app.MapGet("/health/replication", async (
            ReplicationHealthService health,
            CancellationToken ct) => Results.Ok(await health.GetAsync(ct).ConfigureAwait(false))).AllowAnonymous();

        return app;
    }
}
