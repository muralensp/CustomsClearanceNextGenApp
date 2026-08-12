namespace CustomsClearance.Platform.Messaging;

/// <summary>
/// A subscriber for events carrying TPayload. Multiple handlers may exist
/// for the same payload type - InMemoryEventBus invokes all of them.
/// </summary>
public interface IEventHandler<TPayload>
{
    Task HandleAsync(DomainEventEnvelope<TPayload> envelope, CancellationToken cancellationToken = default);
}
