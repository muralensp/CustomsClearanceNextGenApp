using CustomsClearance.Platform.Messaging;

namespace CustomsClearance.Platform.Notifications;

/// <summary>Payload of the "NotificationRequested" domain event (EventType constant below).</summary>
public sealed record NotificationRequestedPayload(string Recipient, string Subject, string Body, string Type, string? EmailAddress);

/// <summary>
/// Event-driven entry point: any module publishes NotificationRequested and
/// this handler turns it into a persisted notification (and email). Works
/// the same over the RabbitMQ bus and the InMemory fallback.
/// </summary>
public sealed class NotificationRequestedHandler : IEventHandler<NotificationRequestedPayload>
{
    public const string EventType = "NotificationRequested";

    private readonly NotificationDispatcher _dispatcher;

    public NotificationRequestedHandler(NotificationDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task HandleAsync(DomainEventEnvelope<NotificationRequestedPayload> envelope, CancellationToken cancellationToken = default)
    {
        var p = envelope.Payload;
        await _dispatcher.DispatchAsync(
            new NotificationMessage(p.Recipient, p.Subject, p.Body, p.Type, p.EmailAddress),
            envelope.EventId,
            cancellationToken);
    }
}
