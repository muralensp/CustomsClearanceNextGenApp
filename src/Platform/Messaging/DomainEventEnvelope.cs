using CustomsClearance.Platform.Logging;

namespace CustomsClearance.Platform.Messaging;

/// <summary>
/// The unified event envelope from Day6_API_Standards_v1.1.docx Section
/// 3.1 - every domain/integration event carries this shape, with the
/// payload as the event-specific data. EventVersion follows the same
/// major.minor discipline as REST APIs (Section 3): a major bump for
/// breaking payload changes, minor for additive fields.
/// </summary>
public sealed record DomainEventEnvelope<TPayload>(
    Guid EventId,
    string EventType,
    string EventVersion,
    DateTimeOffset OccurredAt,
    string? CorrelationId,
    string Source,
    TPayload Payload)
{
    /// <summary>
    /// Preferred construction path: auto-stamps EventId, OccurredAt (UTC),
    /// and CorrelationId (from CorrelationIdAccessor.Current), matching
    /// the pattern already established by AuditEvent.Create()
    /// (Platform.Audit, Day 17) for the same reason - one thread ties REST
    /// calls, events, and audit records together.
    /// </summary>
    public static DomainEventEnvelope<TPayload> Create(string eventType, string eventVersion, string source, TPayload payload)
    {
        return new DomainEventEnvelope<TPayload>(
            EventId: Guid.NewGuid(),
            EventType: eventType,
            EventVersion: eventVersion,
            OccurredAt: DateTimeOffset.UtcNow,
            CorrelationId: CorrelationIdAccessor.Current,
            Source: source,
            Payload: payload);
    }
}
