using System.Text.Json;

namespace CustomsClearance.Platform.Audit.Persistence;

/// <summary>
/// The stored shape of an AuditEvent: one append-only row. FieldChanges are
/// kept as a JSON text column (not a child table, not jsonb) - they are read
/// back together with their event and never queried individually, and plain
/// text stays portable across providers (ADR-0001).
/// </summary>
public sealed class AuditEventRecord
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public Guid AuditEventId { get; set; }

    public string? CorrelationId { get; set; }

    public AuditEventCategory EventCategory { get; set; }

    public required string EntityType { get; set; }

    public Guid? EntityId { get; set; }

    public required string Action { get; set; }

    public Guid? PerformedBy { get; set; }

    public string? PerformedBySystem { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string? BusinessRuleRef { get; set; }

    public string? FieldChangesJson { get; set; }

    public DataClassification Classification { get; set; }

    public static AuditEventRecord From(AuditEvent e) => new()
    {
        AuditEventId = e.AuditEventId,
        CorrelationId = e.CorrelationId,
        EventCategory = e.EventCategory,
        EntityType = e.EntityType,
        EntityId = e.EntityId,
        Action = e.Action,
        PerformedBy = e.PerformedBy,
        PerformedBySystem = e.PerformedBySystem,
        OccurredAt = e.OccurredAt,
        BusinessRuleRef = e.BusinessRuleRef,
        FieldChangesJson = e.FieldChanges is { Count: > 0 } ? JsonSerializer.Serialize(e.FieldChanges, _json) : null,
        Classification = e.Classification,
    };

    public AuditEvent ToEvent() => new(
        AuditEventId,
        CorrelationId,
        EventCategory,
        EntityType,
        EntityId,
        Action,
        PerformedBy,
        PerformedBySystem,
        OccurredAt,
        BusinessRuleRef,
        FieldChangesJson is null ? null : JsonSerializer.Deserialize<List<FieldChange>>(FieldChangesJson, _json),
        Classification);
}
