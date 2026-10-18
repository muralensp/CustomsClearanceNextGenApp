// Api host. Cross-cutting platform wiring lives here; every capability (Identity,
// Rules admin, Configuration, Notifications, Audit, Reference data, Diagnostics)
// is an IApiModule under Modules/ that owns its own service registration,
// development-time schema bootstrap and route mapping. Modules are discovered by
// ApiModuleCatalog, so adding one does not mean editing this file - which is the
// point: this file used to be the one place every change touched (863 lines), and
// two parallel changes could not both edit it without conflicting.
//
// History of what was wired in here, by day: Day 13 placeholder Api host; Day 16
// Platform.Logging/ErrorHandling; Day 17 Platform.Security/Audit; Day 18
// Platform.Persistence/Messaging; Day 23 Identity; Day 24 RabbitMQ; Day 26-28
// structured logging, metrics, tracing; Day 31-35 Rules engine; Day 36
// Configuration; Day 37 Notifications; Day 38 durable audit; Day 39 Reference
// data. See CLAUDE.md and adr/ for the decisions behind each.

using CustomsClearance.Api.Modules;
using CustomsClearance.Platform.Audit;
using CustomsClearance.Platform.ErrorHandling;
using CustomsClearance.Platform.Logging;
using CustomsClearance.Platform.Messaging;
using CustomsClearance.Platform.Observability;
using CustomsClearance.Platform.Security;

var builder = WebApplication.CreateBuilder(args);

var modules = ApiModuleCatalog.Discover();

// Day 26: structured (JSON) console logging, for centralized log
// aggregation (Loki/Promtail/Grafana, see docker-compose.yml).
builder.Logging.AddCustomsClearanceStructuredLogging();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Day 34 finding: Minimal API's default JSON binding rejects enum values
// sent as strings (e.g. "category": "DeMinimis") - System.Text.Json's
// built-in enum converter expects a number unless a JsonStringEnumConverter
// is registered. Found for real via the first live POST /api/rules call
// (a raw BadHttpRequestException, not a clean validation error). Since the
// Day-Wise Plan's own deliverable wording is "testable via Postman/
// Swagger" - a human typing "DeMinimis" into Swagger, not an enum's
// integer value - string enum binding is required, not optional.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddCustomsClearanceAuthentication(builder.Configuration);
builder.Services.AddCustomsClearanceAuthorization();
builder.Services.AddCustomsClearanceTokenIssuance(builder.Configuration);
builder.Services.AddCustomsClearanceAudit();
builder.Services.AddCustomsClearanceMetrics("CustomsClearance.Api");

// Day 28: distributed tracing, exported via OTLP to Tempo (docker-
// compose.yml's "tempo" service). Same "Host=tempo" (compose DNS) vs.
// "Host=localhost" (dotnet run) split as every other infra dependency.
builder.Services.AddCustomsClearanceTracing(
    "CustomsClearance.Api",
    builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317");

// Day 24: real event bus (RabbitMQ) when configured, InMemoryEventBus
// otherwise - same "absence means skip" convention as the ConnectionStrings,
// and required by ADR-0009 (integration tests must not require
// infrastructure; TestApiFactory blanks RabbitMq:HostName the same way it
// blanks those connection strings). Each module registers its own event types.
var rabbitMqHostName = builder.Configuration["RabbitMq:HostName"];
if (!string.IsNullOrWhiteSpace(rabbitMqHostName))
{
    builder.Services.AddCustomsClearanceRabbitMqMessaging(builder.Configuration, registry =>
    {
        foreach (var module in modules)
        {
            module.RegisterEventTypes(registry);
        }
    });
}
else
{
    builder.Services.AddCustomsClearanceMessaging();
}

// Capabilities. Runs after the core registrations above on purpose: the Audit
// module replaces the console audit writer AddCustomsClearanceAudit() registered.
foreach (var module in modules)
{
    module.ConfigureServices(builder);
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Dev-only: create each module's tables (and seed), in module order. Not a
    // real EF Core migration pipeline - that is later work once real
    // bounded-context entities exist. See Modules/DatabaseBootstrap.cs.
    foreach (var module in modules)
    {
        await module.InitializeAsync(app);
    }
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

// Day 27: Prometheus scrape endpoint ("/metrics" by default).
app.MapCustomsClearanceMetricsEndpoint();

// Health check endpoint - used by the docker-compose smoke test in CI
// (Day 13) to confirm the container actually starts and serves traffic,
// not just that it builds.
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "CustomsClearance.Api" }))
    .WithName("HealthCheck");

// Each module guards its own routes with the same condition as its service
// registration (Minimal API map-time bindability lesson, Day 18).
foreach (var module in modules)
{
    module.MapEndpoints(app);
}

await app.RunAsync();

// Exposed for WebApplicationFactory-based integration tests later.
public partial class Program
{
    protected Program() { }
}
