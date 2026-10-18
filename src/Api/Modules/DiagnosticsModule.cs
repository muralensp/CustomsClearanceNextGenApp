using CustomsClearance.Api.Diagnostics;
using CustomsClearance.Platform.Audit;
using CustomsClearance.Platform.ErrorHandling;
using CustomsClearance.Platform.Messaging;
using CustomsClearance.Platform.Persistence;
using CustomsClearance.Platform.Security;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Api.Modules;

/// <summary>
/// Development-only diagnostics proving the platform libraries against the
/// real host (Days 16-18): error mapping, the secured endpoint + audit writer,
/// and a real PostgreSQL write + domain-event round trip (widgets). Not real
/// business endpoints - remove or replace once real context endpoints exercise
/// these naturally.
///
/// Order 10: this module's schema bootstrap runs first and creates the
/// physical database the later modules then add their tables to.
/// Enabled (for the DB part) by ConnectionStrings:DiagnosticsDb.
/// </summary>
public sealed class DiagnosticsModule : IApiModule
{
    private const string _connectionName = "DiagnosticsDb";

    public string Name => "Diagnostics";

    public int Order => 10;

    public void RegisterEventTypes(EventTypeRegistry registry) =>
        registry.Register<DiagnosticsWidgetCreatedPayload>("DiagnosticsWidgetCreated");

    public void ConfigureServices(WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(_connectionName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            builder.Services.AddDbContext<DiagnosticsDbContext>(options => options.UseNpgsql(connectionString));
            builder.Services.AddScoped<PlatformDbContext>(sp => sp.GetRequiredService<DiagnosticsDbContext>());
            builder.Services.AddScoped<DiagnosticsWidgetRepository>();
        }

        // Registered unconditionally, as before the Day 40+ refactor.
        builder.Services.AddScoped<IEventHandler<DiagnosticsWidgetCreatedPayload>, DiagnosticsWidgetCreatedHandler>();
    }

    public async Task InitializeAsync(WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(_connectionName)))
        {
            return;
        }

        // Dev-only convenience: EnsureCreated (not a real EF Core migration
        // pipeline - that is later work once real bounded-context entities
        // exist) so /diagnostics/widgets has a table to write to. Plain
        // EnsureCreated only: this module always runs first, so it is the one
        // that creates the database itself.
        await DatabaseBootstrap.WithRetryAsync(app, _connectionName, async () =>
        {
            using var scope = app.Services.CreateScope();
            var diagnosticsDb = scope.ServiceProvider.GetRequiredService<DiagnosticsDbContext>();
            await DatabaseBootstrap.EnsureSchemaAsync(diagnosticsDb, createTablesIfDatabaseExists: false);
        });
    }

    public void MapEndpoints(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

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
        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(_connectionName)))
        {
            return;
        }

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
