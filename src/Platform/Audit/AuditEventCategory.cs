namespace CustomsClearance.Platform.Audit;

/// <summary>
/// Business audit = who did what to which business entity, and why.
/// Technical audit = system-level security events (login, permission
/// change, config change). One AuditEvent model with this category field,
/// rather than two separate systems - per
/// Day10_Security_Audit_Standards_v1.0.docx Section 17.1.
/// </summary>
public enum AuditEventCategory
{
    BusinessAudit,
    TechnicalAudit,
}
