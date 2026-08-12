using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Platform.Persistence;

/// <summary>
/// Base DbContext every bounded context's Infrastructure project derives
/// from. Stamps CreatedAt/UpdatedAt on AuditableEntity-derived entities
/// automatically in SaveChangesAsync, so no context has to remember to do
/// this itself. Per adr/0001, this is the ONLY place EF Core/PostgreSQL
/// specifics are meant to live (Infrastructure-tier) - Domain/Application
/// projects reference IRepository, never DbContext directly.
/// </summary>
public abstract class PlatformDbContext : DbContext
{
    protected PlatformDbContext(DbContextOptions options) : base(options)
    {
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is not IAuditableEntityMarker)
            {
                continue;
            }

            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(AuditableEntity<object>.CreatedAt)).CurrentValue = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(AuditableEntity<object>.UpdatedAt)).CurrentValue = now;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Non-generic marker so SaveChangesAsync can detect any AuditableEntity&lt;T&gt;
/// regardless of its TId, without reflection over open generic types.
/// </summary>
public interface IAuditableEntityMarker
{
}
