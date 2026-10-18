using CustomsClearance.Platform.Persistence;

namespace CustomsClearance.Platform.Notifications;

/// <summary>
/// One notification addressed to a recipient (a username). This row IS the
/// in-app notification; each attempt to push it out over another channel
/// (email today) is recorded as a NotificationDelivery.
/// </summary>
public sealed class Notification : AuditableEntity<Guid>
{
    public required string Recipient { get; set; }

    public required string Subject { get; set; }

    public required string Body { get; set; }

    /// <summary>Free-form category, e.g. "ConfigChanged" - lets a UI group or filter later.</summary>
    public string Type { get; set; } = "General";

    public string? EmailAddress { get; set; }

    /// <summary>
    /// EventId of the domain event that produced this notification. Unique,
    /// so a redelivered event (RabbitMQ is at-least-once) does not create a
    /// duplicate notification or send a second email.
    /// </summary>
    public Guid? SourceEventId { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    public List<NotificationDelivery> Deliveries { get; set; } = new();
}

public enum DeliveryStatus
{
    Delivered,
    Failed,
    Skipped,
}

public sealed class NotificationDelivery : AuditableEntity<Guid>
{
    public Guid NotificationId { get; set; }

    public required string Channel { get; set; }

    public DeliveryStatus Status { get; set; }

    public string? Error { get; set; }
}
