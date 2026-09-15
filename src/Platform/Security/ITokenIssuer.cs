namespace CustomsClearance.Platform.Security;

/// <summary>
/// Issues JWTs for an authenticated user, symmetric-key-signed per
/// JwtOptions (see that class's doc comment for the Day 23 /
/// future-Authority-migration framing). The counterpart to
/// AddCustomsClearanceAuthentication's Bearer *validation* - this is
/// *issuance*, used by a login endpoint (Day 23) once a caller's
/// credentials have already been verified elsewhere (this interface takes
/// no password; that check happens before calling it).
/// </summary>
public interface ITokenIssuer
{
    IssuedToken IssueToken(Guid userId, string username, string role);
}

public sealed record IssuedToken(string AccessToken, DateTime ExpiresAtUtc);
