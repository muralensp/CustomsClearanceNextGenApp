using CustomsClearance.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomsClearance.Platform.Notifications.Tests;

public class NotificationRequestedHandlerTests
{
    [Fact]
    public async Task HandleAsync_PersistsNotificationFromPayload_KeyedByEventId()
    {
        using var db = TestHost.NewDb(nameof(HandleAsync_PersistsNotificationFromPayload_KeyedByEventId));
        var email = new FakeEmailSender();
        var handler = new NotificationRequestedHandler(TestHost.NewDispatcher(db, email));
        var envelope = DomainEventEnvelope<NotificationRequestedPayload>.Create(
            NotificationRequestedHandler.EventType,
            "1.0",
            "Test",
            new NotificationRequestedPayload("supervisor", "Hello", "World", "Greeting", "sup@example.com"));

        await handler.HandleAsync(envelope);

        var saved = await db.Notifications.Include(n => n.Deliveries).SingleAsync();
        Assert.Equal("supervisor", saved.Recipient);
        Assert.Equal("Hello", saved.Subject);
        Assert.Equal("World", saved.Body);
        Assert.Equal("Greeting", saved.Type);
        Assert.Equal(envelope.EventId, saved.SourceEventId);
        Assert.Equal(2, saved.Deliveries.Count);
        Assert.Single(email.Sent);
    }
}

public class ChannelTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a@b.com", true)]
    public void EmailChannel_AppliesOnlyWhenAddressPresent(string? address, bool expected)
    {
        var channel = new EmailNotificationChannel(new FakeEmailSender());

        Assert.Equal(expected, channel.AppliesTo(new NotificationMessage("u", "s", "b", "t", address)));
    }

    [Fact]
    public void InAppChannel_AlwaysApplies()
    {
        Assert.True(new InAppNotificationChannel().AppliesTo(new NotificationMessage("u", "s", "b", "t", null)));
    }

    [Fact]
    public async Task DisabledEmailSender_IsNotConfigured_AndThrowsIfUsed()
    {
        var sender = new DisabledEmailSender();

        Assert.False(sender.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync("a@b.com", "s", "b", CancellationToken.None));
    }
}

public class ModelAndRegistrationTests
{
    [Fact]
    public void Model_EnforcesUniqueSourceEventId_AndCascadesDeliveries()
    {
        using var db = TestHost.NewDb(nameof(Model_EnforcesUniqueSourceEventId_AndCascadesDeliveries));
        var entity = db.Model.FindEntityType(typeof(Notification))!;

        var index = entity.GetIndexes().Single(i => i.Properties.Single().Name == nameof(Notification.SourceEventId));
        Assert.True(index.IsUnique);

        var fk = entity.GetNavigations().Single(n => n.Name == nameof(Notification.Deliveries)).ForeignKey;
        Assert.Equal(DeleteBehavior.Cascade, fk.DeleteBehavior);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDbContext<NotificationsDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddCustomsClearanceNotifications(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void WithEmailHost_RegistersSmtpSenderWithParsedOptions()
    {
        using var provider = Build(new()
        {
            ["Email:Host"] = "mail.local",
            ["Email:Port"] = "2525",
            ["Email:From"] = "from@x.com",
            ["Email:EnableSsl"] = "true",
        });

        Assert.IsType<SmtpEmailSender>(provider.GetRequiredService<IEmailSender>());
        Assert.Equal(new EmailOptions("mail.local", 2525, "from@x.com", true), provider.GetRequiredService<EmailOptions>());
    }

    [Fact]
    public void WithEmailHost_UsesDefaultsForMissingOrInvalidSettings()
    {
        using var provider = Build(new() { ["Email:Host"] = "mail.local", ["Email:Port"] = "not-a-number" });

        Assert.Equal(
            new EmailOptions("mail.local", 25, "noreply@customsclearance.local", false),
            provider.GetRequiredService<EmailOptions>());
    }

    [Fact]
    public void WithoutEmailHost_RegistersDisabledSender()
    {
        using var provider = Build(new());

        Assert.IsType<DisabledEmailSender>(provider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void Registration_ResolvesDispatcherHandlerAndBothChannels()
    {
        using var provider = Build(new());
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<NotificationDispatcher>());
        Assert.IsType<NotificationRequestedHandler>(scope.ServiceProvider.GetRequiredService<IEventHandler<NotificationRequestedPayload>>());
        Assert.Equal(
            new[] { "Email", "InApp" },
            scope.ServiceProvider.GetServices<INotificationChannel>().Select(c => c.Name).OrderBy(n => n).ToArray());
    }
}
