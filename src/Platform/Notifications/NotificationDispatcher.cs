using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomsClearance.Platform.Notifications;

/// <summary>
/// Turns a NotificationMessage into one persisted Notification plus one
/// NotificationDelivery per applicable channel. A channel that throws is
/// recorded as Failed and never stops the other channels or loses the
/// in-app notification - the record is saved either way.
/// </summary>
public sealed class NotificationDispatcher
{
    private readonly NotificationsDbContext _dbContext;
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        NotificationsDbContext dbContext,
        IEnumerable<INotificationChannel> channels,
        ILogger<NotificationDispatcher> logger)
    {
        _dbContext = dbContext;
        _channels = channels;
        _logger = logger;
    }

    public async Task<Notification> DispatchAsync(NotificationMessage message, Guid? sourceEventId, CancellationToken cancellationToken = default)
    {
        if (sourceEventId is { } eventId)
        {
            var existing = await _dbContext.Notifications
                .Include(n => n.Deliveries)
                .SingleOrDefaultAsync(n => n.SourceEventId == eventId, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Event {EventId} already produced notification {NotificationId}; skipping redelivery", eventId, existing.Id);
                return existing;
            }
        }

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            Recipient = message.Recipient,
            Subject = message.Subject,
            Body = message.Body,
            Type = message.Type,
            EmailAddress = message.EmailAddress,
            SourceEventId = sourceEventId,
        };

        foreach (var channel in _channels.Where(c => c.AppliesTo(message)))
        {
            ChannelOutcome outcome;
            try
            {
                outcome = await channel.SendAsync(notification, message, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Day 41 (ADR-0036): SMTP errors can echo the recipient address in
                // their message. The address is personal data (DPDPA 2023), so it is
                // redacted before the message is logged or stored on the delivery
                // row (which the Admin API returns). The exception object itself is
                // not passed to the logger for the same reason; its type is.
                var safeError = RedactAddress(ex.Message, message.EmailAddress);
#pragma warning disable S6667 // deliberate: the exception object (message + inner exceptions) may echo the address, see above
                _logger.LogWarning(
                    "Notification channel {Channel} failed for notification {NotificationId}: {ExceptionType}: {Error}",
                    channel.Name,
                    notification.Id,
                    ex.GetType().Name,
                    safeError);
#pragma warning restore S6667
                outcome = new ChannelOutcome(DeliveryStatus.Failed, safeError);
            }

            notification.Deliveries.Add(new NotificationDelivery
            {
                Id = Guid.NewGuid(),
                NotificationId = notification.Id,
                Channel = channel.Name,
                Status = outcome.Status,
                Error = outcome.Error,
            });
        }

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Notification {NotificationId} for {Recipient} dispatched: {Deliveries}",
            notification.Id,
            notification.Recipient,
            string.Join(", ", notification.Deliveries.Select(d => $"{d.Channel}={d.Status}")));

        return notification;
    }

    internal static string RedactAddress(string text, string? emailAddress) =>
        string.IsNullOrWhiteSpace(emailAddress)
            ? text
            : text.Replace(emailAddress, "[redacted-email]", StringComparison.OrdinalIgnoreCase);
}
