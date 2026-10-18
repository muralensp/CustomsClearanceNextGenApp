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
                _logger.LogWarning(ex, "Notification channel {Channel} failed for notification {NotificationId}", channel.Name, notification.Id);
                outcome = new ChannelOutcome(DeliveryStatus.Failed, ex.Message);
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
}
