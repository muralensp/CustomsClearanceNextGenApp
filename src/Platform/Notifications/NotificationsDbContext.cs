using CustomsClearance.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Platform.Notifications;

/// <summary>
/// Day 37's schema. Same real-PostgreSQL-via-EnsureCreatedAsync pattern as
/// ConfigDbContext (Day 36) and RulesDbContext (Day 31).
/// </summary>
public sealed class NotificationsDbContext : PlatformDbContext
{
    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : base(options)
    {
    }

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Notification>(n =>
        {
            n.Property(x => x.Recipient).HasMaxLength(200);
            n.Property(x => x.Subject).HasMaxLength(300);
            n.Property(x => x.Type).HasMaxLength(100);

            // "My notifications" lookups.
            n.HasIndex(x => x.Recipient);

            // Idempotency key for redelivered events; NULLs are not
            // considered duplicates by PostgreSQL's unique index.
            n.HasIndex(x => x.SourceEventId).IsUnique();

            n.HasMany(x => x.Deliveries)
                .WithOne()
                .HasForeignKey(d => d.NotificationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationDelivery>(d =>
        {
            d.Property(x => x.Channel).HasMaxLength(50);
            d.Property(x => x.Status).HasConversion<string>();
        });
    }
}
