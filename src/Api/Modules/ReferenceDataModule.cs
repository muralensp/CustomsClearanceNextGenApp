using CustomsClearance.Api.ReferenceData;
using CustomsClearance.Platform.ReferenceData;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Api.Modules;

/// <summary>
/// Day 39: reference/lookup tables seeded with PLACEHOLDER data (ADR-0033).
/// Enabled by ConnectionStrings:ReferenceDataDb.
/// </summary>
public sealed class ReferenceDataModule : IApiModule
{
    private const string _connectionName = "ReferenceDataDb";

    public string Name => "ReferenceData";

    public int Order => 70;

    public void ConfigureServices(WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(_connectionName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            builder.Services.AddDbContext<ReferenceDataDbContext>(options => options.UseNpgsql(connectionString));
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
            var referenceDb = scope.ServiceProvider.GetRequiredService<ReferenceDataDbContext>();

            await DatabaseBootstrap.EnsureSchemaAsync(referenceDb);

            // Insert-missing-only, so safe on every start.
            var seeded = await ReferenceDataSeeder.SeedAsync(referenceDb);
            app.Logger.LogInformation(
                "Reference data seeded: {Countries} countries, {Currencies} currencies, {Units} units inserted (placeholder data)",
                seeded.Countries,
                seeded.Currencies,
                seeded.Units);
        });
    }

    public void MapEndpoints(WebApplication app)
    {
        if (!string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(_connectionName)))
        {
            app.MapReferenceDataEndpoints();
        }
    }
}
