using CustomsClearance.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Api.Diagnostics;

/// <summary>
/// Development-only entity/DbContext proving Platform.Persistence works
/// against the real PostgreSQL container (docker-compose, Day 13), not
/// just EF Core's InMemory provider (which is what the unit tests use).
/// Not a real bounded context - remove once a real context's
/// Infrastructure project exercises PlatformDbContext/EfRepository
/// naturally.
/// </summary>
public sealed class DiagnosticsWidget : AuditableEntity<Guid>
{
    public required string Name { get; set; }
}

public sealed class DiagnosticsDbContext : PlatformDbContext
{
    public DiagnosticsDbContext(DbContextOptions<DiagnosticsDbContext> options) : base(options)
    {
    }

    public DbSet<DiagnosticsWidget> Widgets => Set<DiagnosticsWidget>();
}

public sealed class DiagnosticsWidgetRepository : EfRepository<DiagnosticsWidget, Guid>
{
    public DiagnosticsWidgetRepository(PlatformDbContext dbContext) : base(dbContext)
    {
    }
}
