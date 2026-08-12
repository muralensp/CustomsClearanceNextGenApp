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
}
