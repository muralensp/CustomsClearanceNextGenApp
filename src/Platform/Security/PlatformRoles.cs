namespace CustomsClearance.Platform.Security;

/// <summary>
/// Role names, matching the Day 1 Permissions Matrix
/// (Day1_Bounded_Context_Map_v2.docx, PRD Ch.4.9) exactly - these are the
/// role claim values expected in a JWT's role claim, and the single
/// source of truth every context's [Authorize(Roles = ...)] or policy
/// should reference, rather than re-typing role strings per context.
/// </summary>
public static class PlatformRoles
{
    public const string Broker = "Broker";
    public const string Supervisor = "Supervisor";
    public const string ComplianceOfficer = "ComplianceOfficer";
    public const string Admin = "Admin";
}
