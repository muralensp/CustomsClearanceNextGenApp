using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomsClearance.Platform.Notifications.Tests;

internal sealed class FakeEmailSender : IEmailSender
{
    public bool IsConfigured { get; init; } = true;

    public Exception? ThrowOnSend { get; init; }

    public List<(string To, string Subject, string Body)> Sent { get; } = new();

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        if (ThrowOnSend is not null)
        {
            throw ThrowOnSend;
        }

        Sent.Add((to, subject, body));
        return Task.CompletedTask;
    }
}

internal static class TestHost
{
    public static NotificationsDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>().UseInMemoryDatabase(name).Options);

    public static NotificationDispatcher NewDispatcher(NotificationsDbContext db, IEmailSender emailSender, params INotificationChannel[] extraChannels)
    {
        var channels = new List<INotificationChannel> { new InAppNotificationChannel(), new EmailNotificationChannel(emailSender) };
        channels.AddRange(extraChannels);
        return new NotificationDispatcher(db, channels, NullLogger<NotificationDispatcher>.Instance);
    }
}

public class NotificationDispatcherTests
{
    private static NotificationMessage Message(string? email = null) =>
        new("broker", "Subject", "Body", "Test", email);

    [Fact]
    public async Task WithoutEmailAddress_PersistsInAppNotificationOnly()
    {
        using var db = TestHost.NewDb(nameof(WithoutEmailAddress_PersistsInAppNotificationOnly));
        var email = new FakeEmailSender();
        var dispatcher = TestHost.NewDispatcher(db, email);

        var notification = await dispatcher.DispatchAsync(Message(), sourceEventId: null);

        var saved = await db.Notifications.Include(n => n.Deliveries).SingleAsync();
        Assert.Equal(notification.Id, saved.Id);
        Assert.Equal("broker", saved.Recipient);
        Assert.Null(saved.ReadAt);
        var delivery = Assert.Single(saved.Deliveries);
        Assert.Equal("InApp", delivery.Channel);
        Assert.Equal(DeliveryStatus.Delivered, delivery.Status);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task WithEmailAddress_SendsEmailAndRecordsBothDeliveries()
    {
        using var db = TestHost.NewDb(nameof(WithEmailAddress_SendsEmailAndRecordsBothDeliveries));
        var email = new FakeEmailSender();
        var dispatcher = TestHost.NewDispatcher(db, email);

        await dispatcher.DispatchAsync(Message("broker@example.com"), sourceEventId: null);

        var sent = Assert.Single(email.Sent);
        Assert.Equal(("broker@example.com", "Subject", "Body"), sent);
        var saved = await db.Notifications.Include(n => n.Deliveries).SingleAsync();
        Assert.Equal(new[] { "Email", "InApp" }, saved.Deliveries.Select(d => d.Channel).OrderBy(c => c).ToArray());
        Assert.All(saved.Deliveries, d => Assert.Equal(DeliveryStatus.Delivered, d.Status));
    }

    [Fact]
    public async Task EmailNotConfigured_RecordsSkipped_NotFailed()
    {
        using var db = TestHost.NewDb(nameof(EmailNotConfigured_RecordsSkipped_NotFailed));
        var email = new FakeEmailSender { IsConfigured = false };
        var dispatcher = TestHost.NewDispatcher(db, email);

        await dispatcher.DispatchAsync(Message("broker@example.com"), sourceEventId: null);

        var saved = await db.Notifications.Include(n => n.Deliveries).SingleAsync();
        var emailDelivery = saved.Deliveries.Single(d => d.Channel == "Email");
        Assert.Equal(DeliveryStatus.Skipped, emailDelivery.Status);
        Assert.NotNull(emailDelivery.Error);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task EmailFailure_IsRecorded_AndInAppNotificationStillSaved()
    {
        using var db = TestHost.NewDb(nameof(EmailFailure_IsRecorded_AndInAppNotificationStillSaved));
        var email = new FakeEmailSender { ThrowOnSend = new InvalidOperationException("smtp down") };
        var dispatcher = TestHost.NewDispatcher(db, email);

        await dispatcher.DispatchAsync(Message("broker@example.com"), sourceEventId: null);

        var saved = await db.Notifications.Include(n => n.Deliveries).SingleAsync();
        var emailDelivery = saved.Deliveries.Single(d => d.Channel == "Email");
        Assert.Equal(DeliveryStatus.Failed, emailDelivery.Status);
        Assert.Equal("smtp down", emailDelivery.Error);
        Assert.Equal(DeliveryStatus.Delivered, saved.Deliveries.Single(d => d.Channel == "InApp").Status);
    }

    [Fact]
    public async Task EmailFailure_ThatEchoesTheAddress_StoresARedactedError()
    {
        // Day 41: the recipient address is personal data; an SMTP error that echoes
        // it must not end up on the delivery row (returned by the Admin API).
        using var db = TestHost.NewDb(nameof(EmailFailure_ThatEchoesTheAddress_StoresARedactedError));
        var email = new FakeEmailSender { ThrowOnSend = new InvalidOperationException("Mailbox Broker@Example.com unavailable") };
        var dispatcher = TestHost.NewDispatcher(db, email);

        await dispatcher.DispatchAsync(Message("broker@example.com"), sourceEventId: null);

        var saved = await db.Notifications.Include(n => n.Deliveries).SingleAsync();
        var error = saved.Deliveries.Single(d => d.Channel == "Email").Error;
        Assert.Equal("Mailbox [redacted-email] unavailable", error);
    }

    [Fact]
    public async Task OneFailingChannel_DoesNotStopOtherChannels()
    {
        using var db = TestHost.NewDb(nameof(OneFailingChannel_DoesNotStopOtherChannels));
        var dispatcher = TestHost.NewDispatcher(db, new FakeEmailSender(), new ThrowingChannel());

        await dispatcher.DispatchAsync(Message("broker@example.com"), sourceEventId: null);

        var saved = await db.Notifications.Include(n => n.Deliveries).SingleAsync();
        Assert.Equal(DeliveryStatus.Failed, saved.Deliveries.Single(d => d.Channel == "Boom").Status);
        Assert.Equal(DeliveryStatus.Delivered, saved.Deliveries.Single(d => d.Channel == "Email").Status);
    }

    [Fact]
    public async Task RedeliveredEvent_DoesNotCreateSecondNotificationOrEmail()
    {
        using var db = TestHost.NewDb(nameof(RedeliveredEvent_DoesNotCreateSecondNotificationOrEmail));
        var email = new FakeEmailSender();
        var dispatcher = TestHost.NewDispatcher(db, email);
        var eventId = Guid.NewGuid();

        var first = await dispatcher.DispatchAsync(Message("broker@example.com"), eventId);
        var second = await dispatcher.DispatchAsync(Message("broker@example.com"), eventId);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.Notifications.CountAsync());
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task NoEventId_EachCallCreatesANewNotification()
    {
        using var db = TestHost.NewDb(nameof(NoEventId_EachCallCreatesANewNotification));
        var dispatcher = TestHost.NewDispatcher(db, new FakeEmailSender());

        await dispatcher.DispatchAsync(Message(), sourceEventId: null);
        await dispatcher.DispatchAsync(Message(), sourceEventId: null);

        Assert.Equal(2, await db.Notifications.CountAsync());
    }

    private sealed class ThrowingChannel : INotificationChannel
    {
        public string Name => "Boom";

        public bool AppliesTo(NotificationMessage message) => true;

        public Task<ChannelOutcome> SendAsync(Notification notification, NotificationMessage message, CancellationToken cancellationToken)
            => throw new InvalidOperationException("channel exploded");
    }
}
