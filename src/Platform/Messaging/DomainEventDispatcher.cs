using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CustomsClearance.Platform.Messaging;

/// <summary>
/// The reflection-based dispatch RabbitMqEventConsumer needs because
/// TPayload isn't known until a message arrives (unlike InMemoryEventBus,
/// where the publish call site knows TPayload statically). Extracted as
/// its own class - separate from the RabbitMQ connection/channel plumbing
/// - so this, the highest-risk part of Day 24 (generic type resolution +
/// reflection invocation), can be unit-tested without a real broker
/// connection. Same separation-of-concerns reasoning as ErrorMapper being
/// tested apart from ExceptionHandlingMiddleware's transport plumbing
/// (Day 16).
/// </summary>
public sealed class DomainEventDispatcher
{
    private readonly EventTypeRegistry _eventTypeRegistry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DomainEventDispatcher> _logger;

    public DomainEventDispatcher(EventTypeRegistry eventTypeRegistry, IServiceScopeFactory scopeFactory, ILogger<DomainEventDispatcher> logger)
    {
        _eventTypeRegistry = eventTypeRegistry;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task DispatchAsync(string eventType, string envelopeJson, CancellationToken cancellationToken = default)
    {
        var payloadType = _eventTypeRegistry.ResolvePayloadType(eventType);
        if (payloadType is null)
        {
            _logger.LogWarning("No registered payload type for EventType {EventType} - skipping (not dispatched).", eventType);
            return;
        }

        var envelopeType = typeof(DomainEventEnvelope<>).MakeGenericType(payloadType);
        var envelope = JsonSerializer.Deserialize(envelopeJson, envelopeType)
            ?? throw new InvalidOperationException($"Deserialized envelope for EventType '{eventType}' was null.");

        using var scope = _scopeFactory.CreateScope();
        var handlerType = typeof(IEventHandler<>).MakeGenericType(payloadType);
        var handlers = scope.ServiceProvider.GetServices(handlerType).ToList();
        var handleMethod = handlerType.GetMethod(nameof(IEventHandler<object>.HandleAsync))
            ?? throw new InvalidOperationException($"HandleAsync not found on {handlerType}.");

        _logger.LogInformation("Dispatching event {EventType} to {HandlerCount} handler(s).", eventType, handlers.Count);

        foreach (var handler in handlers)
        {
            var task = (Task)handleMethod.Invoke(handler, [envelope, cancellationToken])!;
            await task;
        }
    }
}
