using CustomsClearance.Platform.Logging;

namespace CustomsClearance.Platform.Audit;

/// <summary>
/// The single, shared audit event model every bounded context uses -
/// per Day10_Security_Audit_Standards_v1.0.docx Section 7, avoiding the
/// duplication risk of each context building its own bespoke audit
/// mechanism (Day 1 Section 4.1's ownership-boundary note, applied here to
/// audit infrastructure specifically).
/// </summary>
public sealed record AuditEvent(
    Guid AuditEventId,
    string? CorrelationId,
    AuditEventCategory EventCategory,
    string EntityType,
    Guid? EntityId,
    string Action,
    Guid? PerformedBy,
    string? PerformedBySystem,
    DateTimeOffset OccurredAt,
    string? BusinessRuleRef,
    IReadOnlyList<FieldChange>? FieldChanges,
    DataClassification Classification)
{
    /// <summary>
    /// Preferred construction path: auto-stamps AuditEventId, OccurredAt
    /// (UTC), and CorrelationId (from CorrelationIdAccessor.Current, per
    /// adr/0004 Section 10) so callers only supply the parts that actually
    /// vary per event.
    /// </summary>
    public static AuditEvent Create(
        AuditEventCategory eventCategory,
        string entityType,
        string action,
        DataClassification classification,
        Guid? entityId = null,
        Guid? performedBy = null,
        string? performedBySystem = null,
        string? businessRuleRef = null,
        IReadOnlyList<FieldChange>? fieldChanges = null)
    {
        if (performedBy is null && string.IsNullOrWhiteSpace(performedBySystem))
        {
            throw new ArgumentException(
                $"Either {nameof(performedBy)} or {nameof(performedBySystem)} must be supplied - " +
                "every audit event must be attributable to a user or a system.");
        }

        return new AuditEvent(
            AuditEventId: Guid.NewGuid(),
            CorrelationId: CorrelationIdAccessor.Current,
            EventCategory: eventCategory,
            EntityType: entityType,
            EntityId: entityId,
            Action: action,
            PerformedBy: performedBy,
            PerformedBySystem: performedBySystem,
            OccurredAt: DateTimeOffset.UtcNow,
            BusinessRuleRef: businessRuleRef,
            FieldChanges: fieldChanges,
            Classification: classification);
    }
}
