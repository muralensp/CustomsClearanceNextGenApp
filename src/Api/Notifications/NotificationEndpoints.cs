using CustomsClearance.Platform.Notifications;
using CustomsClearance.Platform.Security;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Api.Notifications;

public sealed record DeliveryResponse(string Channel, string Status, string? Error);

public sealed record NotificationResponse(
    Guid Id,
    string Recipient,
    string Subject,
    string Body,
    string Type,
    bool IsRead,
    DateTimeOffset CreatedAt,
    IReadOnlyList<DeliveryResponse> Deliveries);

/// <summary>
/// Day 37's notification read API. Any authenticated user reads and marks
/// read only their OWN notifications (recipient = the JWT's name claim);
/// listing every notification with its delivery outcomes is Admin-only.
/// Notifications are created by events (NotificationRequested), never by
/// a direct API call, so there is deliberately no create endpoint.
/// </summary>
public static class NotificationEndpoints
{
    public static WebApplication MapNotificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/notifications");

        group.MapGet("/mine", ListMineAsync).RequireAuthorization().WithName("ListMyNotifications");
        group.MapPost("/{id:guid}/read", MarkReadAsync).RequireAuthorization().WithName("MarkNotificationRead");
        group.MapGet("/", ListAllAsync).RequireAuthorization(PlatformPolicies.RequireAdmin).WithName("ListAllNotifications");

        return app;
    }

    private static async Task<IResult> ListMineAsync(HttpContext http, NotificationsDbContext dbContext, CancellationToken cancellationToken)
    {
        var username = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        var items = await dbContext.Notifications
            .AsNoTracking()
            .Include(n => n.Deliveries)
            .Where(n => n.Recipient == username)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);

        return Results.Ok(items.Select(ToResponse));
    }

    private static async Task<IResult> MarkReadAsync(Guid id, HttpContext http, NotificationsDbContext dbContext, CancellationToken cancellationToken)
    {
        var username = http.User.Identity?.Name;
        var notification = await dbContext.Notifications
            .Include(n => n.Deliveries)
            .SingleOrDefaultAsync(n => n.Id == id, cancellationToken);

        // Someone else's notification is reported as not found, not forbidden,
        // so ids cannot be probed for existence.
        if (notification is null || notification.Recipient != username)
        {
            return Results.NotFound();
        }

        notification.ReadAt ??= DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToResponse(notification));
    }

    private static async Task<IResult> ListAllAsync(NotificationsDbContext dbContext, CancellationToken cancellationToken)
    {
        var items = await dbContext.Notifications
            .AsNoTracking()
            .Include(n => n.Deliveries)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);

        return Results.Ok(items.Select(ToResponse));
    }

    private static NotificationResponse ToResponse(Notification n) => new(
        n.Id,
        n.Recipient,
        n.Subject,
        n.Body,
        n.Type,
        n.ReadAt is not null,
        n.CreatedAt,
        n.Deliveries.OrderBy(d => d.Channel).Select(d => new DeliveryResponse(d.Channel, d.Status.ToString(), d.Error)).ToList());
}
