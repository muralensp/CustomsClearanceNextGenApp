using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CustomsClearance.Platform.Security;

/// <summary>
/// Default ITokenIssuer: mints a JWT signed with the same symmetric key
/// AddCustomsClearanceAuthentication validates against, so a token issued
/// here is immediately accepted by this same host's Bearer validation.
/// 8-hour expiry is a Day 23 placeholder (no refresh-token flow yet -
/// re-login is the only renewal path for now, acceptable for a Phase 0
/// dev-time Identity stand-in per adr/0017).
/// </summary>
public sealed class JwtTokenIssuer : ITokenIssuer
{
    private static readonly TimeSpan _tokenLifetime = TimeSpan.FromHours(8);

    private readonly JwtOptions _options;

    public JwtTokenIssuer(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public IssuedToken IssueToken(Guid userId, string username, string role)
    {
        var expiresAtUtc = DateTime.UtcNow.Add(_tokenLifetime);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: signingCredentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        return new IssuedToken(accessToken, expiresAtUtc);
    }
}
