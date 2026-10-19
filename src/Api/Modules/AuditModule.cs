using CustomsClearance.Api.AuditTrail;
using CustomsClearance.Platform.Audit.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CustomsClearance.Api.Modules;

/// <summary>
/// Day 38: durable, append-only audit store (ADR-0032). When
/// ConnectionStrings:AuditDb is present, EfAuditWriter replaces the
/// ConsoleAuditWriter that Program.cs registers via AddCustomsClearanceAudit()
/// (core registrations run before any module); absent (integration tests),
/// audit events keep going to the console, so tests stay infrastructure-free.
///
/// Day 41 (ADR-0036): the runtime connection (AuditDb) is a RESTRICTED role that
/// the database only lets INSERT and SELECT on the audit table. Creating the
/// table and the grants needs an owner, supplied separately as
/// ConnectionStrings:AuditAdminDb (used at schema-bootstrap time only; the
/// running app never holds it in a real environment). Without AuditAdminDb the
/// module falls back to the pre-Day-41 behaviour (bootstrap over AuditDb, no
/// database-level enforcement) and says so in the log.
/// </summary>
public sealed class AuditModule : IApiModule
{
    private const string _connectionName = "AuditDb";
    private const string _adminConnectionName = "AuditAdminDb";

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

        var adminConnectionString = app.Configuration.GetConnectionString(_adminConnectionName);
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            app.Logger.LogWarning(
                "ConnectionStrings:{Admin} is not set: the audit table is bootstrapped over the runtime connection and " +
                "the database does NOT enforce append-only (no restricted role). Development convenience only (ADR-0036).",
                _adminConnectionName);

            await DatabaseBootstrap.WithRetryAsync(app, _connectionName, async () =>
            {
                // The audit store is a singleton over IDbContextFactory (its own
                // connection), not a scoped DbContext.
                var auditDbFactory = app.Services.GetRequiredService<IDbContextFactory<AuditDbContext>>();
                await using var auditDb = await auditDbFactory.CreateDbContextAsync();
                await DatabaseBootstrap.EnsureSchemaAsync(auditDb);
            });
            return;
        }

        // Owner connection: create the table, make sure the restricted runtime
        // role exists, then (re)apply INSERT+SELECT-only grants. All idempotent.
        var runtimeLogin = new NpgsqlConnectionStringBuilder(app.Configuration.GetConnectionString(_connectionName));
        var runtimeRole = runtimeLogin.Username!;
        var ownerOptions = new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(adminConnectionString).Options;

        await DatabaseBootstrap.WithRetryAsync(app, _adminConnectionName, async () =>
        {
            await using var ownerDb = new AuditDbContext(ownerOptions);
            await DatabaseBootstrap.EnsureSchemaAsync(ownerDb);
            await EnsureRuntimeRoleExistsAsync(adminConnectionString, runtimeRole, runtimeLogin.Password);
            await AuditStoreGrants.ApplyAsync(ownerDb, runtimeRole);
            app.Logger.LogInformation("Audit store: role {Role} limited to INSERT and SELECT on the audit table.", runtimeRole);
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

    /// <summary>
    /// Development-only provisioning of the restricted login role. This is
    /// PostgreSQL DDL (CREATE ROLE has no portable form), kept here in the host's
    /// provider-aware bootstrap rather than in the platform library (ADR-0001);
    /// in a real environment the role is provisioned by IaC / a DBA.
    /// </summary>
    private static async Task EnsureRuntimeRoleExistsAsync(string adminConnectionString, string role, string? password)
    {
        if (string.IsNullOrEmpty(password) || role.Contains('"'))
        {
            throw new InvalidOperationException("The AuditDb connection string must name a Username (no quotes) and a Password.");
        }

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();

        await using (var exists = new NpgsqlCommand("SELECT 1 FROM pg_roles WHERE rolname = @name", connection))
        {
            exists.Parameters.AddWithValue("name", role);
            if (await exists.ExecuteScalarAsync() is not null)
            {
                return;
            }
        }

        // CREATE ROLE cannot take bind parameters; the password literal is escaped.
        var literal = password.Replace("'", "''", StringComparison.Ordinal);
        await using var create = new NpgsqlCommand(
            $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{literal}' NOSUPERUSER NOCREATEDB NOCREATEROLE",
            connection);
        await create.ExecuteNonQueryAsync();
    }
}
