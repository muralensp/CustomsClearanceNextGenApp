namespace CustomsClearance.Platform.Messaging;

/// <summary>
/// Port for publishing domain/integration events. Kept transport-agnostic
/// (per adr/0006's Clean Architecture adaptation, same pattern as
/// Platform.Audit's IAuditWriter) - the real transport (Kafka/RabbitMQ/
/// Azure Service Bus, per PRD Ch.3 references and the Day 5 event catalog)
/// is a Phase 0 infrastructure decision. InMemoryEventBus (this library)
/// is a functional, same-process placeholder, not the production
/// implementation - see its own doc comment.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<TPayload>(DomainEventEnvelope<TPayload> envelope, CancellationToken cancellationToken = default);
}
