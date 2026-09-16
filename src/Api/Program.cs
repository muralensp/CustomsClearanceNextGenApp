// Placeholder Api host (Day 13). No business logic yet - exists solely so the
// CD pipeline has something real to containerize and smoke-test, mirroring
// the approach used for the CI pipeline's placeholder scaffold (Day 11).
// Replace/extend as each bounded context's endpoints are implemented,
// per ADR-0003's bounded-context-first structure.
//
// Day 16 wires in the shared Platform.Logging and Platform.ErrorHandling
// libraries. Day 17 adds Platform.Security (JWT auth + role policies) and
// Platform.Audit (shared audit trail). Day 18 adds Platform.Persistence
// (EF Core base repository/DbContext) and Platform.Messaging (in-process
// pub/sub), each proven against this real host rather than only in
// isolated unit tests.

using CustomsClearance.Api.Diagnostics;
using CustomsClearance.Api.Identity;
using CustomsClearance.Platform.Audit;
using CustomsClearance.Platform.ErrorHandling;
using CustomsClearance.Platform.Logging;
using CustomsClearance.Platform.Messaging;
using CustomsClearance.Platform.Persistence;
using CustomsClearance.Platform.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Day 26: structured (JSON) console logging, for centralized log
// aggregation (Loki/Promtail/Grafana, see docker-compose.yml).
builder.Logging.AddCustomsClearanceStructuredLogging();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCustomsClearanceAuthentication(builder.Configuration);
builder.Services.AddCustomsClearanceAuthorization();
builder.Services.AddCustomsClearanceTokenIssuance(builder.Configuration);
builder.Services.AddCustomsClearanceAudit();

// Day 24: real event bus (RabbitMQ) when configured, InMemoryEventBus
// otherwise - same "absence means skip" convention as ConnectionStrings:
// DiagnosticsDb/IdentityDb, and required by ADR-0009 (integration tests
// must not require infrastructure; TestApiFactory blanks RabbitMq:HostName
// the same way it blanks those connection strings).
var rabbitMqHostName = builder.Configuration["RabbitMq:HostName"];
if (!string.IsNullOrWhiteSpace(rabbitMqHostName))
{
    builder.Services.AddCustomsClearanceRabbitMqMessaging(builder.Configuration, registry =>
    {
        registry.Register<DiagnosticsWidgetCreatedPayload>("DiagnosticsWidgetCreated");
    });
}
else
{
    builder.Services.AddCustomsClearanceMessaging();
}

// Development-only: DiagnosticsDbContext proves Platform.Persistence
// against the real PostgreSQL container (docker-compose, Day 13). A real
// bounded context's Infrastructure project would register its own
// DbContext the same way, pointed at its own connection string.
var connectionString = builder.Configuration.GetConnectionString("DiagnosticsDb");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<DiagnosticsDbContext>(options => options.UseNpgsql(connectionString));
    builder.Services.AddScoped<PlatformDbContext>(sp => sp.GetRequiredService<DiagnosticsDbContext>());
    builder.Services.AddScoped<DiagnosticsWidgetRepository>();
}

// Day 23: IdentityDbContext, same real-PostgreSQL-placeholder status as
// DiagnosticsDbContext above - a separate connection string (still the
// same physical dev database by default; see appsettings.Development.json)
// so this can later point at a dedicated store without touching Diagnostics.
var identityConnectionString = builder.Configuration.GetConnectionString("IdentityDb");
if (!string.IsNullOrWhiteSpace(identityConnectionString))
{
    builder.Services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(identityConnectionString));
}

builder.Services.AddScoped<IEventHandler<DiagnosticsWidgetCreatedPayload>, DiagnosticsWidgetCreatedHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Dev-only convenience: EnsureCreated (not a real EF Core migration
    // pipeline - that's Phase 0/3 work once real bounded-context entities
    // exist) so /diagnostics/widgets has a table to write to.
    //
    // Retries with backoff: found for real (Day 18) that docker-compose's
    // "depends_on: - postgres" only waits for the container to start, not
    // for Postgres to finish its first-time init (which involves an
    // internal restart cycle) and actually accept connections - an
    // unhandled connection failure here crashed the whole Api process
    // before it could even serve /health. Fixed at the orchestration level
    // too (docker-compose.yml's postgres healthcheck + depends_on
    // condition: service_healthy), but this retry is kept as defense in
    // depth against any remaining brief connection hiccup.
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var diagnosticsDb = scope.ServiceProvider.GetRequiredService<DiagnosticsDbContext>();
                await diagnosticsDb.Database.EnsureCreatedAsync();
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                app.Logger.LogWarning(ex, "DiagnosticsDb not ready (attempt {Attempt}/{MaxAttempts}), retrying in 2s...", attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    // Same retry-with-backoff reasoning as DiagnosticsDb above (Day 18's
    // real docker-compose startup-race finding) - EnsureCreatedAsync, then
    // seed the 4 dev-persona users (Day 23) if the table is empty.
    if (!string.IsNullOrWhiteSpace(identityConnectionString))
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

                // EnsureCreatedAsync only creates the physical DATABASE if
                // missing - it no-ops (does not add tables) when the
                // database already exists, which it does here: Diagnostics
                // (above) already created it. Found for real (Day 23): the
                // Api crashed with "relation Users does not exist" on
                // first login, because IdentityDbContext's own
                // EnsureCreatedAsync silently skipped table creation.
                // Falling back to the relational database creator's
                // CreateTablesAsync (this context's tables only), tolerating
                // "already exists" on a container restart against a
                // persisted volume.
                var created = await identityDb.Database.EnsureCreatedAsync();
                if (!created)
                {
                    var databaseCreator = (Microsoft.EntityFrameworkCore.Storage.RelationalDatabaseCreator)
                        identityDb.GetService<Microsoft.EntityFrameworkCore.Storage.IDatabaseCreator>();
                    try
                    {
                        await databaseCreator.CreateTablesAsync();
                    }
                    catch (Npgsql.PostgresException ex) when (ex.SqlState == Npgsql.PostgresErrorCodes.DuplicateTable)
                    {
                        // Already created by a previous run against this
                        // same persisted volume - fine, not an error.
                    }
                }

                await IdentitySeeder.SeedIfEmptyAsync(identityDb);
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                app.Logger.LogWarning(ex, "IdentityDb not ready (attempt {Attempt}/{MaxAttempts}), retrying in 2s...", attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
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

// Health check endpoint - used by the docker-compose smoke test in CI
// (Day 13) to confirm the container actually starts and serves traffic,
// not just that it builds.
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "CustomsClearance.Api" }))
    .WithName("HealthCheck");

// Day 23: login endpoint - real capability (not a diagnostic), gated the
// same way as the widgets endpoints below (RequestDelegateFactory validates
// parameter bindability at MAP time, so this must only be mapped when
// IdentityDbContext is actually registered).
if (!string.IsNullOrWhiteSpace(identityConnectionString))
{
    app.MapPost("/auth/login", async (LoginRequest request, IdentityDbContext identityDb, ITokenIssuer tokenIssuer) =>
    {
        var user = await identityDb.Users.SingleOrDefaultAsync(u => u.Username == request.Username);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var hasher = new PasswordHasher<PlatformUser>();
        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return Results.Unauthorized();
        }

        var issued = tokenIssuer.IssueToken(user.Id, user.Username, user.Role);
        return Results.Ok(new LoginResponse(issued.AccessToken, issued.ExpiresAtUtc, user.Role));
    }).WithName("Login");
}

// Development-only diagnostic endpoints proving the platform libraries work
// end-to-end against the real host (Days 16-18) - not real business
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

    // Only mapped when DiagnosticsDbContext is actually registered (i.e. a
    // connection string is configured). Minimal API's RequestDelegateFactory
    // validates that every parameter is bindable (as a service, in this
    // case) at MAP time, not per-request - so mapping these routes with an
    // unregistered DbContext parameter crashes the ENTIRE app at startup,
    // not just these two endpoints. Found for real (Day 18): integration
    // tests that don't configure a database (per adr/0009, they must not
    // require infrastructure) failed to start the host at all, including
    // for completely unrelated endpoints like /health, until this guard
    // was added.
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        // Proves Platform.Persistence (real PostgreSQL, via DiagnosticsDbContext)
        // and Platform.Messaging (publishes an event that
        // DiagnosticsWidgetCreatedHandler receives) together in one endpoint.
        app.MapPost("/diagnostics/widgets", async (
            string name,
            DiagnosticsWidgetRepository repository,
            DiagnosticsDbContext dbContext,
            IEventPublisher eventPublisher) =>
        {
            var widget = new DiagnosticsWidget { Id = Guid.NewGuid(), Name = name };
            await repository.AddAsync(widget);
            await dbContext.SaveChangesAsync();

            var envelope = DomainEventEnvelope<DiagnosticsWidgetCreatedPayload>.Create(
                eventType: "DiagnosticsWidgetCreated",
                eventVersion: "1.0",
                source: "DiagnosticsEndpoint",
                payload: new DiagnosticsWidgetCreatedPayload(widget.Id, widget.Name));
            await eventPublisher.PublishAsync(envelope);

            return Results.Ok(new { widget.Id, widget.Name, widget.CreatedAt, eventId = envelope.EventId });
        }).WithName("DiagnosticsCreateWidget");

        app.MapGet("/diagnostics/widgets/{id:guid}", async (Guid id, DiagnosticsWidgetRepository repository) =>
        {
            var widget = await repository.GetByIdAsync(id);
            return widget is null ? Results.NotFound() : Results.Ok(widget);
        }).WithName("DiagnosticsGetWidget");
    }
}

await app.RunAsync();

// Exposed for WebApplicationFactory-based integration tests later.
public partial class Program
{
    protected Program() { }
}
