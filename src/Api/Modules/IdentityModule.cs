using CustomsClearance.Api.Identity;
using CustomsClearance.Platform.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Api.Modules;

/// <summary>
/// Day 23: login + dev-persona users. Enabled by ConnectionStrings:IdentityDb.
/// </summary>
public sealed class IdentityModule : IApiModule
{
    private const string _connectionName = "IdentityDb";

    public string Name => "Identity";

    public int Order => 20;

    public void ConfigureServices(WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(_connectionName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            builder.Services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(connectionString));
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
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            await DatabaseBootstrap.EnsureSchemaAsync(identityDb);
            await IdentitySeeder.SeedIfEmptyAsync(identityDb);
        });
    }

    public void MapEndpoints(WebApplication app)
    {
        // Login is a real capability (not a diagnostic). RequestDelegateFactory
        // validates parameter bindability at MAP time, so this must only be
        // mapped when IdentityDbContext is actually registered.
        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(_connectionName)))
        {
            return;
        }

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
}
