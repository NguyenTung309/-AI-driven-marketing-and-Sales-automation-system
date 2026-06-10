using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Clawbot.Infrastructure.Observability;

// OpenTelemetry tracing + metrics. ASP.NET Core instrumentation emits the
// http.server.request.duration histogram → per-route p95 (NFR-01 chat/analytics latency);
// HttpClient instrumentation traces downstream calls (Pancake/Meta/LLM); runtime metrics for GC/threads.
// Console exporter (opt-in via Otel:Console=true) for dev. OTLP exporter deferred until an
// audit-clean version is pinned — NuGetAudit gate flagged OTLP 1.9.0 (GHSA-4625-4j76-fww9).
public static class TelemetryModule
{
    public static IServiceCollection AddClawbotTelemetry(
        this IServiceCollection services, IConfiguration cfg, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        var consoleExport = string.Equals(cfg["Otel:Console"], "true", StringComparison.OrdinalIgnoreCase);

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
                if (consoleExport) t.AddConsoleExporter();
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation();
                if (consoleExport) m.AddConsoleExporter();
            });

        return services;
    }
}
