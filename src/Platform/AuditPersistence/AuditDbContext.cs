using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Platform.Audit.Persistence;

/// <summary>
/// Append-only audit store (adr/0004: "A single AuditEvent model,
/// append-only"). Immutability is enforced at the application level here:
/// any attempt to update or delete a stored row throws before it reaches the
/// database. Database-level enforcement (revoked UPDATE/DELETE grants or
/// triggers) is provider-specific and is deferred to Day 41 Security
/// Hardening (see ADR-0032) - until then, anyone with direct database
/// access can still alter rows.
///
/// Deliberately a plain DbContext, not PlatformDbContext: that base stamps
/// UpdatedAt on modification, which an append-only table must never have.
/// </summary>
public sealed class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options)
    {
    }

    public DbSet<AuditEventRecord> AuditEvents => Set<AuditEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEventRecord>(e =>
        {
            e.HasKey(x => x.AuditEventId);
            e.Property(x => x.EntityType).HasMaxLength(200);
            e.Property(x => x.Action).HasMaxLength(200);
            e.Property(x => x.CorrelationId).HasMaxLength(100);
            e.Property(x => x.PerformedBySystem).HasMaxLength(200);
            e.Property(x => x.BusinessRuleRef).HasMaxLength(200);
            e.Property(x => x.EventCategory).HasConversion<string>();
            e.Property(x => x.Classification).HasConversion<string>();

            // "History of this entity" - the dominant audit question.
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.PerformedBy);
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => x.CorrelationId);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnsureAppendOnly()
    {
        var offending = ChangeTracker.Entries<AuditEventRecord>()
            .FirstOrDefault(e => e.State is EntityState.Modified or EntityState.Deleted);
        if (offending is not null)
        {
            throw new InvalidOperationException(
                $"Audit events are append-only: refusing to {(offending.State == EntityState.Deleted ? "delete" : "modify")} " +
                $"audit event {offending.Entity.AuditEventId}.");
        }
    }
}
