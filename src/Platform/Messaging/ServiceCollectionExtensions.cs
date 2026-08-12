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
}
