using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CustomsClearance.Platform.Messaging;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers InMemoryEventBus as the default IEventPublisher. Call
    /// again with a different registration (e.g. a future
    /// KafkaEventPublisher in an Infrastructure project) to override once
    /// a real broker is chosen - this is intentionally the seam for that,
    /// same pattern as AddCustomsClearanceAudit (Platform.Audit, Day 17).
    /// </summary>
    public static IServiceCollection AddCustomsClearanceMessaging(this IServiceCollection services)
    {
        services.AddSingleton<IEventPublisher, InMemoryEventBus>();
        return services;
    }

    /// <summary>
    /// The real swap ADR-0012 anticipated: registers RabbitMqEventBus (a
    /// real, cross-process IEventPublisher) and RabbitMqEventConsumer (a
    /// BackgroundService consuming from this host's queue) instead of
    /// InMemoryEventBus, per Day 24. Overrides any prior
    /// AddCustomsClearanceMessaging() call - call this one instead, not in
    /// addition, when a real broker is configured (see RabbitMqOptions'
    /// doc comment on the "HostName present" convention).
    ///
    /// configureEventTypes registers every payload type a consumer on this
    /// host needs to resolve from the wire (see EventTypeRegistry) - the
    /// caller (the Api host's composition root) is responsible for this,
    /// same as it's responsible for registering its own IEventHandler&lt;T&gt;
    /// implementations.
    /// </summary>
    public static IServiceCollection AddCustomsClearanceRabbitMqMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<EventTypeRegistry> configureEventTypes)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        var registry = new EventTypeRegistry();
        configureEventTypes(registry);
        services.AddSingleton(registry);

        services.AddSingleton<IEventPublisher, RabbitMqEventBus>();
        services.AddSingleton<DomainEventDispatcher>();
        services.AddHostedService<RabbitMqEventConsumer>();

        return services;
    }
}
