namespace CustomsClearance.Platform.Messaging;

/// <summary>
/// Maps DomainEventEnvelope.EventType (a string, per Day6_API_Standards_
/// v1.1.docx Section 3.1) to its CLR payload type, so RabbitMqEventConsumer
/// (which receives only the wire message - no compile-time TPayload) can
/// deserialize into the right DomainEventEnvelope&lt;TPayload&gt; and
/// resolve the right IEventHandler&lt;TPayload&gt; from DI. Every publisher
/// of a real (non-InMemory) event must register its payload type here -
/// an unregistered EventType arriving on the queue is logged and skipped
/// (not silently dropped without trace), per Day 24's "sample event flow"
/// scope: this is not a schema registry, just enough to route.
/// </summary>
public sealed class EventTypeRegistry
{
    private readonly Dictionary<string, Type> _payloadTypesByEventType = new();

    public void Register<TPayload>(string eventType)
    {
        _payloadTypesByEventType[eventType] = typeof(TPayload);
    }

    public Type? ResolvePayloadType(string eventType) =>
        _payloadTypesByEventType.GetValueOrDefault(eventType);
}
