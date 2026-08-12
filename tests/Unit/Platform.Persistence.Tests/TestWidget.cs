using CustomsClearance.Platform.Persistence;

namespace CustomsClearance.Platform.Persistence.Tests;

/// <summary>
/// A minimal stand-in entity used only to exercise AuditableEntity/
/// EfRepository/PlatformDbContext generically, since no real bounded
/// context entity exists yet to test against.
/// </summary>
public sealed class TestWidget : AuditableEntity<Guid>
{
    public required string Name { get; set; }
}

public sealed class TestDbContext : PlatformDbContext
{
    public TestDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    public Microsoft.EntityFrameworkCore.DbSet<TestWidget> Widgets => Set<TestWidget>();
}

public sealed class TestWidgetRepository : EfRepository<TestWidget, Guid>
{
    public TestWidgetRepository(PlatformDbContext dbContext) : base(dbContext)
    {
    }
}
