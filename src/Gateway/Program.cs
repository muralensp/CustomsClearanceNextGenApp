// Day 22 - API Gateway. Routes external traffic to the src/Api host via YARP
// (Yarp.ReverseProxy), per Day6_API_Standards_v1.1.docx Section 2: the
// gateway is the sole external ingress point; the Api host itself is not
// published on the docker-compose network's host ports (see
// docker-compose.yml).
//
// Day 25 - Base Security Framework: the gateway now performs its own JWT
// Bearer validation (Platform.Security), not just header passthrough.
// Routes carrying an "AuthorizationPolicy" in ReverseProxy:Routes config
// (appsettings.json) are rejected with 401/403 here, before ever reaching
// the Api host - genuine defense-in-depth, not a duplicate of the Api
// host's own enforcement (which still runs too; both layers use the same
// PlatformPolicies/PlatformRoles from Platform.Security, so a token valid
// at the gateway is valid at the Api host and vice versa). Routes with no
// AuthorizationPolicy configured remain open at the gateway layer - the
// Api host is still the source of truth for which of ITS endpoints
// require auth; the gateway only pre-empts the ones explicitly marked.
//
// Correlation ID origination moves here too: this is the true request
// ingress point, so CorrelationIdAccessor.Current (Platform.Logging) should
// be set at the edge and forwarded via the X-Correlation-Id header - YARP
// forwards all non-restricted request headers by default, so no extra
// wiring is needed for that propagation.

using System.Threading.RateLimiting;
using CustomsClearance.Platform.ErrorHandling;
using CustomsClearance.Platform.Logging;
using CustomsClearance.Platform.Security;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Day 26: structured (JSON) console logging, for centralized log
// aggregation (Loki/Promtail/Grafana, see docker-compose.yml).
builder.Logging.AddCustomsClearanceStructuredLogging();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddCustomsClearanceAuthentication(builder.Configuration);
builder.Services.AddCustomsClearanceAuthorization();

// Basic rate limiting (Day 22 deliverable): fixed window per client IP.
// Values are a conservative dev-time default, not a tuned production
// policy - production limits are a Phase 0 Day 26-30 (Observability/
// capacity) concern once real traffic patterns exist.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0
            }));
});

var app = builder.Build();

app.UseCustomsClearanceCorrelationId();
app.UseCustomsClearanceErrorHandling();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// The gateway's own liveness check, distinct from the proxied "/health"
// (which reaches the Api host's own /health per the Day 22 deliverable:
// "gateway routes hello-world service"). Mapped before MapReverseProxy's
// catch-all so routing picks the more specific match first.
app.MapGet("/__gateway/health", () => Results.Ok(new { status = "Healthy", service = "CustomsClearance.Gateway" }));

app.MapReverseProxy();

await app.RunAsync();

// Exposed for WebApplicationFactory-based integration tests later.
public partial class Program
{
    protected Program() { }
}
