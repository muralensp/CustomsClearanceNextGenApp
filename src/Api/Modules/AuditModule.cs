using CustomsClearance.Api.AuditTrail;
using CustomsClearance.Platform.Audit.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Api.Modules;

/// <summary>
/// Day 38: durable, append-only audit store (ADR-0032). When
/// ConnectionStrings:AuditDb is present, EfAuditWriter replaces the
/// ConsoleAuditWriter that Program.cs registers via AddCustomsClearanceAudit()
/// (core registrations run before any module); absent (integration tests),
/// audit events keep going to the console, so tests stay infrastructure-free.
/// </summary>
public sealed class AuditModule : IApiModule
{
    private const string _connectionName = "AuditDb";

    public string Name => "Audit";

    public int Order => 60;

    public void ConfigureServices(WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(_connectionName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            builder.Services.AddCustomsClearanceAuditPersistence(options => options.UseNpgsql(connectionString));
        }
    }

    public async Task InitializeAsync(WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(_connectionName)))
        {
            return;
        }

        await DatabaseBootstrap.WithRetryAsync(app, _connectionName, async () =>
        {
            // The audit store is a singleton over IDbContextFactory (its own
            // connection), not a scoped DbContext.
            var auditDbFactory = app.Services.GetRequiredService<IDbContextFactory<AuditDbContext>>();
            await using var auditDb = await auditDbFactory.CreateDbContextAsync();
            await DatabaseBootstrap.EnsureSchemaAsync(auditDb);
        });
    }

    public void MapEndpoints(WebApplication app)
    {
        // Mapped only when the durable store (and so AuditQueryService) is
        // registered.
        if (!string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(_connectionName)))
        {
            app.MapAuditEndpoints();
        }
    }
}
