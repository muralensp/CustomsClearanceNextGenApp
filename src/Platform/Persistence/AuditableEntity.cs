namespace CustomsClearance.Platform.Persistence;

/// <summary>
/// Base class for entities that need creation/modification tracking.
/// CreatedAt/UpdatedAt are stamped automatically by PlatformDbContext's
/// SaveChangesAsync override - never set them manually. This is separate
/// from the full business/technical AuditEvent trail (Platform.Audit,
/// Day 17); this is lightweight row-level bookkeeping, not the audit log.
/// </summary>
public abstract class AuditableEntity<TId> : IAuditableEntityMarker
{
    public required TId Id { get; init; }

    public DateTimeOffset CreatedAt { get; internal set; }

    public DateTimeOffset? UpdatedAt { get; internal set; }
}
