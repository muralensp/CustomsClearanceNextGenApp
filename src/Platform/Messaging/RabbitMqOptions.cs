namespace CustomsClearance.Platform.Messaging;

/// <summary>
/// Bound from the "RabbitMq" configuration section (Day 24). Presence of
/// HostName is this library's own signal (see ServiceCollectionExtensions)
/// for whether to wire RabbitMqEventBus instead of the InMemoryEventBus
/// placeholder - same "absence means skip" convention as
/// ConnectionStrings:DiagnosticsDb/IdentityDb (Days 13/23).
/// </summary>
public class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = string.Empty;

    public int Port { get; set; } = 5672;

    public string UserName { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    /// <summary>Topic exchange every domain event is published to, routed by EventType.</summary>
    public string ExchangeName { get; set; } = "customsclearance.domain-events";

    /// <summary>Single durable queue this host's consumer binds to (Day 24 scope: one queue, not per-context routing).</summary>
    public string QueueName { get; set; } = "customsclearance.domain-events.api";
}
