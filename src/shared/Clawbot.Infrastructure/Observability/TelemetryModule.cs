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
    internal const string HttpServerRequestDurationInstrumentName = "http.server.request.duration";
    internal const double HttpServerDurationSloSeconds = 30d;

    private static readonly double[] HttpServerDurationHistogramBoundariesSeconds =
    {
        0.005d, 0.01d, 0.025d, 0.05d, 0.075d, 0.1d,
        0.25d, 0.5d, 0.75d, 1d, 2.5d, 5d, 7.5d,
        10d, 15d, HttpServerDurationSloSeconds, 60d
    };

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
                m.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddView(
                        HttpServerRequestDurationInstrumentName,
                        CreateHttpServerDurationHistogramConfiguration());

                if (consoleExport) m.AddConsoleExporter();
            });

        return services;
    }

    internal static ExplicitBucketHistogramConfiguration CreateHttpServerDurationHistogramConfiguration() =>
        new() { Boundaries = HttpServerDurationHistogramBoundariesSeconds };
}
