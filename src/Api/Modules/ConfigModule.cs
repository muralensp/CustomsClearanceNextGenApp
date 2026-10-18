using CustomsClearance.Api.ConfigAdmin;
using CustomsClearance.Platform.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Api.Modules;

/// <summary>
/// Day 36: central, runtime-changeable feature flags / business parameters
/// (ADR-0030). Enabled by ConnectionStrings:ConfigDb.
/// </summary>
public sealed class ConfigModule : IApiModule
{
    private const string _connectionName = "ConfigDb";

    public string Name => "Configuration";

    public int Order => 40;

    public void ConfigureServices(WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(_connectionName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            builder.Services.AddDbContext<ConfigDbContext>(options => options.UseNpgsql(connectionString));
            builder.Services.AddCustomsClearanceConfiguration();
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
            using var scope = app.Services.CreateScope();
            await DatabaseBootstrap.EnsureSchemaAsync(scope.ServiceProvider.GetRequiredService<ConfigDbContext>());
        });
    }

    public void MapEndpoints(WebApplication app)
    {
        // Mapped only when ConfigDbContext is registered (Minimal API map-time
        // bindability lesson).
        if (!string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(_connectionName)))
        {
            app.MapConfigAdminEndpoints();
        }
    }
}
