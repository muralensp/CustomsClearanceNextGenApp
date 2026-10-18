using CustomsClearance.Platform.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CustomsClearance.Platform.Notifications;

/// <summary>
/// Registers the notification pipeline behind NotificationsDbContext (the
/// caller registers that, connection-string-presence decides, as for every
/// other DbContext). Email is real SMTP when Email:Host is configured,
/// otherwise the email channel records Skipped.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCustomsClearanceNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        var host = configuration["Email:Host"];
        if (!string.IsNullOrWhiteSpace(host))
        {
            var port = int.TryParse(configuration["Email:Port"], out var parsedPort) ? parsedPort : 25;
            var from = configuration["Email:From"] ?? "noreply@customsclearance.local";
            var enableSsl = bool.TryParse(configuration["Email:EnableSsl"], out var ssl) && ssl;
            services.AddSingleton(new EmailOptions(host, port, from, enableSsl));
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, DisabledEmailSender>();
        }

        services.AddScoped<INotificationChannel, InAppNotificationChannel>();
        services.AddScoped<INotificationChannel, EmailNotificationChannel>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<IEventHandler<NotificationRequestedPayload>, NotificationRequestedHandler>();

        return services;
    }
}
