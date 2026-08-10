using Clawbot.Api.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Clawbot.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
        app.MapGet("/health/ready", ReadinessAsync).AllowAnonymous();

        app.MapGet("/health/channels/pancake", async (
            ChannelHealthService health,
            CancellationToken ct) => Results.Ok(await health.GetPancakeAsync(ct).ConfigureAwait(false))).AllowAnonymous();

        app.MapGet("/health/replication", async (
            ReplicationHealthService health,
            CancellationToken ct) => Results.Ok(await health.GetAsync(ct).ConfigureAwait(false))).AllowAnonymous();

        return app;
    }

    private static async Task<IResult> ReadinessAsync(
        HealthCheckService healthChecks,
        CancellationToken cancellationToken)
    {
        var report = await healthChecks.CheckHealthAsync(
            check => check.Tags.Contains("ready", StringComparer.Ordinal),
            cancellationToken).ConfigureAwait(false);
        var checks = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Status.ToString().ToLowerInvariant());
        var response = new { status = report.Status.ToString().ToLowerInvariant(), checks };
        return report.Status == HealthStatus.Healthy
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
