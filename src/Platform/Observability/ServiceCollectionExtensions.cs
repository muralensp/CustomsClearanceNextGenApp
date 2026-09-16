using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

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
}
