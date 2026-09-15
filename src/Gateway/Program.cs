// Day 22 - API Gateway. Routes external traffic to the src/Api host via YARP
// (Yarp.ReverseProxy), per Day6_API_Standards_v1.1.docx Section 2: the
// gateway is the sole external ingress point; the Api host itself is not
// published on the docker-compose network's host ports (see
// docker-compose.yml). Internal auth (JWT, Platform.Security) still happens
// at the Api host - the gateway passes the Authorization header through
// unmodified, it does not perform auth itself (basic auth passthrough,
// per the Day 22 deliverable).
//
// Correlation ID origination moves here too: this is the true request
// ingress point, so CorrelationIdAccessor.Current (Platform.Logging) should
// be set at the edge and forwarded via the X-Correlation-Id header - YARP
// forwards all non-restricted request headers by default, so no extra
// wiring is needed for that propagation.

using System.Threading.RateLimiting;
using CustomsClearance.Platform.ErrorHandling;
using CustomsClearance.Platform.Logging;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

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
