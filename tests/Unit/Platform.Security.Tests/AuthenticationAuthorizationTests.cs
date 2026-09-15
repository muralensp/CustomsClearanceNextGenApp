using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CustomsClearance.Platform.Security;
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

    private static Task<int> InvokeSecuredEndpointAsync(string? bearerToken) =>
        TestAuthHost.InvokeSecuredEndpointAsync(bearerToken, _testKey, _testIssuer, _testAudience);

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
