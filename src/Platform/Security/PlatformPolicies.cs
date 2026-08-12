namespace CustomsClearance.Platform.Security;

/// <summary>
/// Named authorization policies, registered by
/// AddCustomsClearanceAuthorization(). Endpoints reference these policy
/// names (via [Authorize(Policy = PlatformPolicies.RequireAdmin)]) rather
/// than hardcoding role lists, so the actual role membership per policy is
/// defined once and can evolve (e.g. if a new role is added) without
/// touching every endpoint.
/// </summary>
public static class PlatformPolicies
{
    /// <summary>Admin only.</summary>
    public const string RequireAdmin = "RequireAdmin";

    /// <summary>Supervisor or Admin - matches the Day 1 Permissions Matrix
    /// rows where Supervisor and Admin share the same access (e.g. Delete
    /// Draft Shipment, Override Validation, Configure Rules for Admin only -
    /// see individual context policies for finer distinctions).</summary>
    public const string RequireSupervisorOrAdmin = "RequireSupervisorOrAdmin";

    /// <summary>Compliance Officer or Admin.</summary>
    public const string RequireComplianceOfficerOrAdmin = "RequireComplianceOfficerOrAdmin";

    /// <summary>Any authenticated platform role (Broker, Supervisor,
    /// ComplianceOfficer, or Admin) - the baseline "must be logged in as
    /// someone with a recognized role" check.</summary>
    public const string RequireAnyPlatformRole = "RequireAnyPlatformRole";
}
