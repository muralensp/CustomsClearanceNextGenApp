namespace CustomsClearance.Platform.Security;

/// <summary>
/// Bound from the "Jwt" configuration section. Symmetric-key validation
/// (Key) is the local/Development mode - it lets this library be built,
/// tested, and wired into the Api host now, before the real Identity
/// service exists (Phase 0, Day 23). Once that service is built, the
/// intended migration is to switch to Authority-based validation
/// (issuer-hosted signing keys via OIDC discovery) - a configuration
/// change to how the JwtBearer options are built in
/// ServiceCollectionExtensions, not a rewrite of any code that consumes
/// authentication/authorization (roles, policies, [Authorize] attributes
/// are all unaffected by this future switch).
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Symmetric signing key for local/Development token validation. NEVER
    /// a real secret in source control - see adr/0004 Item 5 (secrets
    /// management). The default in appsettings.Development.json is a
    /// clearly-marked placeholder; production configuration must override
    /// this via a managed secrets store, or (once available) switch to
    /// Authority-based validation entirely, per the class doc above.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Access-token lifetime in minutes (Day 45, ADR-0041). There is no
    /// refresh-token flow yet, so re-login is the only renewal path; the
    /// default is 60 (it was a hard-coded 8 hours). Day 10 specifies 15
    /// minutes plus refresh, which needs the real identity provider.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 60;

    public const int MaxAccessTokenMinutes = 1440;

    /// <summary>
    /// Fails fast on a lifetime that would mint tokens that are already
    /// expired or that live far longer than the standard allows.
    /// </summary>
    public void ValidateLifetime()
    {
        if (AccessTokenMinutes is < 1 or > MaxAccessTokenMinutes)
        {
            throw new InvalidOperationException(
                $"Jwt:AccessTokenMinutes must be between 1 and {MaxAccessTokenMinutes} (got {AccessTokenMinutes}).");
        }
    }
}
