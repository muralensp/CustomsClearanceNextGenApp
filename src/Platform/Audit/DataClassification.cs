namespace CustomsClearance.Platform.Audit;

/// <summary>
/// The four-tier classification from Day10_Security_Audit_Standards_v1.0.docx
/// Section 6, applied here to tag each AuditEvent with the highest
/// classification among the fields it touches - drives downstream masking
/// decisions (Section 12) when audit records are displayed, not stored.
/// </summary>
public enum DataClassification
{
    Public,
    Internal,
    Confidential,
    HighlyConfidential,
}
