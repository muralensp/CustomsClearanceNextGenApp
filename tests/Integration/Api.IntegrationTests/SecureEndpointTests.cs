using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CustomsClearance.Api.IntegrationTests;

/// <summary>
/// Proves Platform.Security + Platform.Audit (Day 17) work correctly when
/// wired into the real Api host end-to-end, via WebApplicationFactory
/// against /diagnostics/secure - the same scenarios verified manually
/// (curl with minted JWTs) during Day 17, now captured as repeatable
/// automated tests. Token minting here mirrors the pattern in
/// Platform.Security.Tests, using the same dev placeholder key configured
/// in appsettings.Development.json.
/// </summary>
public class SecureEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    // Matches appsettings.Development.json's Jwt:Key - a public, documented
    // dev-only placeholder (adr/0011), not a real secret.
    private const string _devKey = "LOCAL-DEV-ONLY-PLACEHOLDER-KEY-NEVER-USE-IN-PRODUCTION-32B";
    private const string _devIssuer = "https://dev.customsclearance.india";
    private const string _devAudience = "customsclearance-api-dev";

    private readonly WebApplicationFactory<Program> _factory;

    public SecureEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private static string MintToken(string role)
    {
        var claims = new[] { new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.Name, "integration-test-user") };
#pragma warning disable S6781 // dev placeholder key, see _devKey's doc comment
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_devKey));
#pragma warning restore S6781
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_devIssuer, _devAudience, claims, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task NoToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/diagnostics/secure");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminToken_Returns200AndWritesAuditEvent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {MintToken("Admin")}");

        var response = await client.GetAsync("/diagnostics/secure");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("auditEventId", body);
    }

    [Fact]
    public async Task WrongRoleToken_Returns403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {MintToken("Broker")}");

        var response = await client.GetAsync("/diagnostics/secure");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
