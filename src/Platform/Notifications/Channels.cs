namespace CustomsClearance.Platform.Notifications;

/// <summary>What a caller (or event) asks to be notified: who, what, and optionally an email address.</summary>
public sealed record NotificationMessage(string Recipient, string Subject, string Body, string Type, string? EmailAddress);

public sealed record ChannelOutcome(DeliveryStatus Status, string? Error = null);

/// <summary>
/// One way of delivering a notification. NotificationDispatcher runs every
/// channel whose AppliesTo is true and records each outcome; a failing
/// channel never prevents the others (or the in-app record) from succeeding.
/// New channels (SMS, push) are added by implementing this and registering it.
/// </summary>
public interface INotificationChannel
{
    string Name { get; }

    bool AppliesTo(NotificationMessage message);

    Task<ChannelOutcome> SendAsync(Notification notification, NotificationMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// The in-app channel. The Notification row the dispatcher persists IS the
/// in-app delivery, so this channel has nothing further to do - it exists so
/// the in-app delivery is recorded the same way as every other channel's.
/// </summary>
public sealed class InAppNotificationChannel : INotificationChannel
{
    public const string ChannelName = "InApp";

    public string Name => ChannelName;

    public bool AppliesTo(NotificationMessage message) => true;

    public Task<ChannelOutcome> SendAsync(Notification notification, NotificationMessage message, CancellationToken cancellationToken)
        => Task.FromResult(new ChannelOutcome(DeliveryStatus.Delivered));
}

public interface IEmailSender
{
    /// <summary>False when no SMTP host is configured; the email channel then records Skipped rather than Failed.</summary>
    bool IsConfigured { get; }

    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}

public sealed class EmailNotificationChannel : INotificationChannel
{
    public const string ChannelName = "Email";

    private readonly IEmailSender _emailSender;

    public EmailNotificationChannel(IEmailSender emailSender)
    {
        _emailSender = emailSender;
    }

    public string Name => ChannelName;

    public bool AppliesTo(NotificationMessage message) => !string.IsNullOrWhiteSpace(message.EmailAddress);

    public async Task<ChannelOutcome> SendAsync(Notification notification, NotificationMessage message, CancellationToken cancellationToken)
    {
        if (!_emailSender.IsConfigured)
        {
            return new ChannelOutcome(DeliveryStatus.Skipped, "Email is not configured (no Email:Host).");
        }

        await _emailSender.SendAsync(message.EmailAddress!, message.Subject, message.Body, cancellationToken);
        return new ChannelOutcome(DeliveryStatus.Delivered);
    }
}
