using CustomsClearance.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Platform.ReferenceData;

/// <summary>
/// Day 39's schema. Same real-PostgreSQL-via-EnsureCreatedAsync pattern as
/// the other contexts (Config, Notifications, Rules) - no EF Core
/// Migrations pipeline exists in this codebase yet.
/// </summary>
public sealed class ReferenceDataDbContext : PlatformDbContext
{
    public ReferenceDataDbContext(DbContextOptions<ReferenceDataDbContext> options) : base(options)
    {
    }

    public DbSet<Country> Countries => Set<Country>();

    public DbSet<Currency> Currencies => Set<Currency>();

    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Country>(e =>
        {
            Configure(e);
            e.Property(x => x.Alpha3Code).HasMaxLength(3);
            e.HasIndex(x => x.Alpha3Code).IsUnique();
        });

        modelBuilder.Entity<Currency>(e =>
        {
            Configure(e);
            e.Property(x => x.NumericCode).HasMaxLength(3);
        });

        modelBuilder.Entity<UnitOfMeasure>(Configure);
    }

    private static void Configure<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e)
        where T : ReferenceEntity
    {
        e.Property(x => x.Code).HasMaxLength(10);
        e.Property(x => x.Name).HasMaxLength(200);
        e.Property(x => x.Source).HasMaxLength(100);
        e.HasIndex(x => x.Code).IsUnique();
    }
}
