namespace CustomsClearance.Platform.Audit;

/// <summary>
/// One field-level change within an AuditEvent, per
/// Day10_Security_Audit_Standards_v1.0.docx Section 8 (Field-Level Audit).
/// Values are stored as-is here (masking, per Section 12, is a
/// display/API-response concern applied by the consuming context, not a
/// storage-time transformation - the audit record must retain the true
/// value for its own legal/audit purpose).
/// </summary>
public sealed record FieldChange(string FieldName, string? PreviousValue, string? NewValue);
