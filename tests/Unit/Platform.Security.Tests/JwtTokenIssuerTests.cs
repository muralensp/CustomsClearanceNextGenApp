using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CustomsClearance.Platform.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace CustomsClearance.Platform.Security.Tests;

/// <summary>
/// Proves JwtTokenIssuer (Day 23) issues a token that (a) carries the
/// expected sub/name/role claims and (b) is actually accepted by this same
/// library's own Bearer validation (AddCustomsClearanceAuthentication) -
/// not just that a well-formed-looking string comes out.
/// </summary>
public class JwtTokenIssuerTests
{
    private const string _testKey = "this-is-a-test-only-signing-key-not-for-production-use-32bytes";
    private const string _testIssuer = "https://test.customsclearance.india";
    private const string _testAudience = "customsclearance-api-tests";

    private static JwtTokenIssuer CreateIssuer()
    {
        var options = Options.Create(new JwtOptions
        {
            Key = _testKey,
            Issuer = _testIssuer,
            Audience = _testAudience,
        });
        return new JwtTokenIssuer(options);
    }

    [Fact]
    public void IssueToken_ProducesTokenWithExpectedClaims()
    {
        var issuer = CreateIssuer();
        var userId = Guid.NewGuid();

        var issued = issuer.IssueToken(userId, "admin", PlatformRoles.Admin);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.AccessToken);
        Assert.Equal(userId.ToString(), jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("admin", jwt.Claims.Single(c => c.Type == ClaimTypes.Name).Value);
        Assert.Equal(PlatformRoles.Admin, jwt.Claims.Single(c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal(_testIssuer, jwt.Issuer);
        Assert.Equal(_testAudience, jwt.Audiences.Single());
    }

    [Fact]
    public void IssueToken_SetsExpiryInTheFuture()
    {
        var issuer = CreateIssuer();

        var issued = issuer.IssueToken(Guid.NewGuid(), "admin", PlatformRoles.Admin);

        Assert.True(issued.ExpiresAtUtc > DateTime.UtcNow);
        Assert.True(issued.ExpiresAtUtc <= DateTime.UtcNow.AddHours(8).AddMinutes(1));
    }

    [Fact]
    public async Task IssuedToken_IsAcceptedByAddCustomsClearanceAuthentication()
    {
        // Round-trip: a token this class issues must be valid against the
        // same library's own Bearer validation pipeline, not just
        // structurally well-formed.
        var issuer = CreateIssuer();
        var issued = issuer.IssueToken(Guid.NewGuid(), "admin", PlatformRoles.Admin);

        var status = await TestAuthHost.InvokeSecuredEndpointAsync(
            issued.AccessToken, _testKey, _testIssuer, _testAudience);

        Assert.Equal(200, status);
    }
}
