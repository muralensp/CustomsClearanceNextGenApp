using Microsoft.AspNetCore.Builder;

namespace CustomsClearance.Platform.Observability;

public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Maps the Prometheus scrape endpoint (default path "/metrics") the
    /// OpenTelemetry Prometheus exporter (AddCustomsClearanceMetrics)
    /// registers. Public, unauthenticated by design - a Prometheus scrape
    /// endpoint carries operational metrics, not business data, and every
    /// example Prometheus deployment (including this project's own,
    /// docker-compose.yml) expects to reach it directly.
    /// </summary>
    public static WebApplication MapCustomsClearanceMetricsEndpoint(this WebApplication app)
    {
        app.MapPrometheusScrapingEndpoint();
        return app;
    }
}
