using CustomsClearance.Platform.Logging;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CustomsClearance.Platform.Observability;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers OpenTelemetry metrics with ASP.NET Core's built-in HTTP
    /// instrumentation (request count, duration histograms, etc. - no
    /// custom instrumentation code needed for Day 27's "service health/
    /// latency" deliverable) and the Prometheus exporter, which
    /// MapCustomsClearanceMetricsEndpoint (below) exposes as a scrape
    /// endpoint. serviceName becomes the OpenTelemetry Resource's
    /// "service.name" attribute, surfaced as the `job` label Prometheus
    /// scrapes with - this is how a Grafana dashboard distinguishes "api"
    /// metrics from "gateway" metrics on one shared datasource.
    /// </summary>
    public static IServiceCollection AddCustomsClearanceMetrics(this IServiceCollection services, string serviceName)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddPrometheusExporter());

        return services;
    }

    /// <summary>
    /// Registers OpenTelemetry distributed tracing (Day 28) - ASP.NET Core
    /// (inbound requests) + HttpClient (outbound, so the Gateway's proxied
    /// call to the Api host is captured as one child span, not a dangling
    /// trace) instrumentation, exported via OTLP to Tempo
    /// (observability/tempo.yaml, docker-compose.yml's "tempo" service).
    /// Every span is tagged with the existing X-Correlation-Id
    /// (CorrelationIdMiddleware, Day 16) so a trace can be cross-referenced
    /// with the same-request log lines already in Loki (Day 26) - same
    /// correlation key, three observability signals.
    /// </summary>
    public static IServiceCollection AddCustomsClearanceTracing(this IServiceCollection services, string serviceName, string otlpEndpoint)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.EnrichWithHttpRequest = (activity, request) =>
                    {
                        if (request.Headers.TryGetValue(CorrelationIdMiddleware.HeaderName, out var correlationId))
                        {
                            activity.SetTag("correlation_id", correlationId.ToString());
                        }
                    };
                })
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint)));

        return services;
    }
}
