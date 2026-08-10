using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Clawbot.Api.Health;

public sealed class RabbitMqHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("RabbitMq");
        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri)
            || string.IsNullOrWhiteSpace(uri.Host))
            return HealthCheckResult.Unhealthy();

        var port = uri.Port > 0 ? uri.Port : 5672;
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(uri.Host, port, cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(exception: ex);
        }
    }
}
