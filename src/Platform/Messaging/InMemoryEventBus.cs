using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CustomsClearance.Platform.Messaging;

/// <summary>
/// Same-process, in-memory IEventPublisher: resolves all registered
/// IEventHandler&lt;TPayload&gt; implementations from DI and invokes them
/// synchronously. This is a functional placeholder (like
/// Platform.Audit's ConsoleAuditWriter, Day 17) for a real distributed
/// message broker - it proves the publish/subscribe contract and envelope
/// shape work correctly, but does NOT survive a process restart, does NOT
/// cross process/service boundaries, and has no retry/dead-letter
/// semantics. Swapping to Kafka/RabbitMQ/Azure Service Bus later (a Phase
/// 0 infrastructure decision) means providing a new IEventPublisher
/// implementation - the envelope shape and IEventHandler contract used by
/// application code do not change.
/// </summary>
public sealed class InMemoryEventBus : IEventPublisher
{
    // IServiceScopeFactory, not IServiceProvider directly: this bus is
    // registered as a Singleton (it must survive for the app's lifetime,
    // like any message bus would), but IEventHandler<T> implementations
    // are typically Scoped (e.g. they may depend on a DbContext). Resolving
    // a Scoped service from the root provider throws
    // InvalidOperationException - creating a scope per publish call is the
    // correct fix, not a workaround. Found for real (Day 18): the first
    // end-to-end run against the real Api host threw exactly this
    // exception, caught cleanly by Platform.ErrorHandling (Day 16) as a
    // 500 with no leaked detail, then fixed here.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InMemoryEventBus> _logger;

    public InMemoryEventBus(IServiceScopeFactory scopeFactory, ILogger<InMemoryEventBus> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task PublishAsync<TPayload>(DomainEventEnvelope<TPayload> envelope, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IEventHandler<TPayload>>().ToList();

        _logger.LogInformation(
            "Publishing event {EventType} v{EventVersion} ({EventId}) from {Source} to {HandlerCount} handler(s). CorrelationId={CorrelationId}",
            envelope.EventType,
            envelope.EventVersion,
            envelope.EventId,
            envelope.Source,
            handlers.Count,
            envelope.CorrelationId);

        foreach (var handler in handlers)
        {
            await handler.HandleAsync(envelope, cancellationToken);
        }
    }
}
