using CustomsClearance.Api.Notifications;
using CustomsClearance.Platform.Messaging;
using CustomsClearance.Platform.Notifications;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Api.Modules;

/// <summary>
/// Day 37: NotificationRequested event -> persisted in-app notification +
/// optional SMTP email (ADR-0031). Enabled by ConnectionStrings:NotificationsDb.
/// </summary>
public sealed class NotificationsModule : IApiModule
{
    private const string _connectionName = "NotificationsDb";

    public string Name => "Notifications";

    public int Order => 50;

    // Registered whenever a real broker is configured, whether or not this
    // module is enabled - an unregistered EventType arriving on the queue is
    // logged and skipped, which is a worse failure than an unused mapping.
    public void RegisterEventTypes(EventTypeRegistry registry) =>
        registry.Register<NotificationRequestedPayload>(NotificationRequestedHandler.EventType);

    public void ConfigureServices(WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(_connectionName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            builder.Services.AddDbContext<NotificationsDbContext>(options => options.UseNpgsql(connectionString));
            builder.Services.AddCustomsClearanceNotifications(builder.Configuration);
        }
    }

    public async Task InitializeAsync(WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(_connectionName)))
        {
            return;
        }

        await DatabaseBootstrap.WithRetryAsync(app, _connectionName, async () =>
        {
            using var scope = app.Services.CreateScope();
            await DatabaseBootstrap.EnsureSchemaAsync(scope.ServiceProvider.GetRequiredService<NotificationsDbContext>());
        });
    }

    public void MapEndpoints(WebApplication app)
    {
        // Mapped only when NotificationsDbContext is registered (map-time
        // bindability guard, as for every DB-backed API).
        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(_connectionName)))
        {
            return;
        }

        app.MapNotificationEndpoints();

        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        // Dev-only trigger for the deliverable "sample notification fires on
        // a test event" - publishes a real NotificationRequested domain event
        // over the configured bus (RabbitMQ in compose); the handler, not this
        // endpoint, creates the notification and sends the email.
        app.MapPost("/diagnostics/notifications/test-event", async (
            string recipient,
            string? emailAddress,
            IEventPublisher eventPublisher) =>
        {
            var envelope = DomainEventEnvelope<NotificationRequestedPayload>.Create(
                eventType: NotificationRequestedHandler.EventType,
                eventVersion: "1.0",
                source: "DiagnosticsEndpoint",
                payload: new NotificationRequestedPayload(
                    recipient,
                    "Test notification",
                    "This is a sample notification fired by a test event.",
                    "Diagnostics",
                    emailAddress));
            await eventPublisher.PublishAsync(envelope);

            return Results.Ok(new { eventId = envelope.EventId, recipient, emailAddress });
        }).WithName("DiagnosticsFireNotificationEvent");
    }
}
