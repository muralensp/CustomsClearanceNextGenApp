namespace CustomsClearance.Platform.Persistence;

/// <summary>
/// Generic repository port. Per adr/0006's Clean Architecture adaptation,
/// a specific bounded context's Domain/Application project (e.g.
/// Declaration.csproj) would normally define its own narrower repository
/// interface (e.g. IDeclarationRepository); this generic interface is the
/// shared building block those context-specific interfaces can extend or
/// be implemented against, so every context isn't hand-rolling the same
/// CRUD plumbing (per the Day-Wise Plan's "shared repository base
/// classes" wording for Day 18).
/// </summary>
public interface IRepository<TEntity, in TId>
    where TEntity : AuditableEntity<TId>
{
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    void Update(TEntity entity);

    void Remove(TEntity entity);
}
