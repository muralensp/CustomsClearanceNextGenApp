// Placeholder Api host (Day 13). No business logic yet - exists solely so the
// CD pipeline has something real to containerize and smoke-test, mirroring
// the approach used for the CI pipeline's placeholder scaffold (Day 11).
// Replace/extend as each bounded context's endpoints are implemented,
// per ADR-0003's bounded-context-first structure.
//
// Day 16 wires in the shared Platform.Logging and Platform.ErrorHandling
// libraries. Day 17 adds Platform.Security (JWT auth + role policies) and
// Platform.Audit (shared audit trail), proving both against this real host
// rather than only in isolated unit tests.

using CustomsClearance.Platform.Audit;
using CustomsClearance.Platform.ErrorHandling;
using CustomsClearance.Platform.Logging;
using CustomsClearance.Platform.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCustomsClearanceAuthentication(builder.Configuration);
builder.Services.AddCustomsClearanceAuthorization();
builder.Services.AddCustomsClearanceAudit();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Order matters: correlation ID must be established before error handling
// runs, so any mapped error response carries the correct CorrelationId
// (per Platform.ErrorHandling's ApplicationBuilderExtensions doc comment).
// Authentication/authorization run after correlation ID (so an auth
// failure's log line still carries it) and before error handling would
// only matter for exceptions thrown inside auth itself - ASP.NET Core's
// auth middleware returns 401/403 directly rather than throwing, so ordering
// relative to UseCustomsClearanceErrorHandling is not load-bearing here.
app.UseCustomsClearanceCorrelationId();
app.UseCustomsClearanceErrorHandling();
app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint - used by the docker-compose smoke test in CI
// (Day 13) to confirm the container actually starts and serves traffic,
// not just that it builds.
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "CustomsClearance.Api" }))
    .WithName("HealthCheck");

// Development-only diagnostic endpoints proving the platform libraries work
// end-to-end against the real host (Days 16-17) - not real business
// endpoints. Remove/replace once real context endpoints exercise these
// naturally.
if (app.Environment.IsDevelopment())
{
    app.MapGet("/diagnostics/throw-validation-error", () =>
    {
        throw new DomainValidationException("BR-DEMO-001", "This is a demonstration validation failure.", field: "demoField");
    }).WithName("DiagnosticsThrowValidationError");

    app.MapGet("/diagnostics/secure", async (IAuditWriter auditWriter) =>
    {
        var auditEvent = AuditEvent.Create(
            AuditEventCategory.TechnicalAudit,
            entityType: "Diagnostics",
            action: "SecureEndpointAccessed",
            classification: DataClassification.Internal,
            performedBySystem: "DiagnosticsEndpoint");

        await auditWriter.WriteAsync(auditEvent);

        return Results.Ok(new { message = "You are authenticated as Admin.", auditEventId = auditEvent.AuditEventId });
    })
    .RequireAuthorization(PlatformPolicies.RequireAdmin)
    .WithName("DiagnosticsSecure");
}

await app.RunAsync();

// Exposed for WebApplicationFactory-based integration tests later.
public partial class Program
{
    protected Program() { }
}
