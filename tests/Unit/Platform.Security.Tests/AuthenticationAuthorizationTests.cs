using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CustomsClearance.Platform.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CustomsClearance.Platform.Security.Tests;

/// <summary>
/// Proves AddCustomsClearanceAuthentication/AddCustomsClearanceAuthorization
/// actually enforce role-based access, by minting real JWTs (signed with
/// the same test key the pipeline validates against) and sending them
/// through a real TestServer pipeline - not just asserting the policy
/// objects were constructed correctly.
/// </summary>
public class AuthenticationAuthorizationTests
{
    // SonarAnalyzer S6781 ("JWT secret keys should not be disclosed") fires
    // on this key (both at declaration and at each usage below) because it
    // is a hardcoded string literal - correctly so in production code, but
    // this is a hardcoded test-only key used solely to mint tokens for
    // this test class, never a real credential, and never read from
    // configuration. Suppressed with justification per CONTRIBUTING.md's
    // Definition of Done (no unexplained new warnings).
    private const string _testKey = "this-is-a-test-only-signing-key-not-for-production-use-32bytes";
    private const string _testIssuer = "https://test.customsclearance.india";
    private const string _testAudience = "customsclearance-api-tests";

    private static string MintToken(string role)
    {
        var claims = new[] { new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.Name, "test-user") };
#pragma warning disable S6781 // test-only key, see _testKey's doc comment
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_testKey));
#pragma warning restore S6781
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _testIssuer,
            audience: _testAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<int> InvokeSecuredEndpointAsync(string? bearerToken)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    var config = new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Jwt:Key"] = _testKey,
                            ["Jwt:Issuer"] = _testIssuer,
                            ["Jwt:Audience"] = _testAudience,
                        })
                        .Build();

                    services.AddRouting();
                    services.AddCustomsClearanceAuthentication(config);
                    services.AddCustomsClearanceAuthorization();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/secure", (HttpContext _) => Results.Ok())
                            .RequireAuthorization(PlatformPolicies.RequireAdmin);
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        if (bearerToken is not null)
        {
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearerToken}");
        }

        var response = await client.GetAsync("/secure");
        return (int)response.StatusCode;
    }

    [Fact]
    public async Task NoToken_Returns401()
    {
        var status = await InvokeSecuredEndpointAsync(bearerToken: null);
        Assert.Equal(401, status);
    }

    [Fact]
    public async Task ValidTokenWithAdminRole_Returns200()
    {
        var token = MintToken(PlatformRoles.Admin);
        var status = await InvokeSecuredEndpointAsync(token);
        Assert.Equal(200, status);
    }

    [Fact]
    public async Task ValidTokenWithWrongRole_Returns403()
    {
        var token = MintToken(PlatformRoles.Broker);
        var status = await InvokeSecuredEndpointAsync(token);
        Assert.Equal(403, status);
    }
}
