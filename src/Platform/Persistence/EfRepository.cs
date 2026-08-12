using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Platform.Persistence;

/// <summary>
/// Generic EF Core implementation of IRepository. Deliberately does NOT
/// call SaveChangesAsync itself (Unit of Work stays with the DbContext,
/// called once by the Application-layer service after all repository
/// operations for a use case complete) - this is the standard Repository
/// + Unit of Work separation, not a "repository that saves on every call"
/// anti-pattern.
/// </summary>
public class EfRepository<TEntity, TId> : IRepository<TEntity, TId>
    where TEntity : AuditableEntity<TId>
{
    protected readonly PlatformDbContext DbContext;
    protected readonly DbSet<TEntity> DbSet;

    public EfRepository(PlatformDbContext dbContext)
    {
        DbContext = dbContext;
        DbSet = dbContext.Set<TEntity>();
    }

    public async Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        return await DbSet.FindAsync(new object?[] { id }, cancellationToken);
    }

    public async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet.ToListAsync(cancellationToken);
    }

    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await DbSet.AddAsync(entity, cancellationToken);
    }

    public void Update(TEntity entity)
    {
        DbSet.Update(entity);
    }

    public void Remove(TEntity entity)
    {
        DbSet.Remove(entity);
    }
}
