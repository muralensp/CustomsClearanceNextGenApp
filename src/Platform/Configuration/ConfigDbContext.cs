using CustomsClearance.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Platform.Configuration;

/// <summary>
/// Day 36's schema. Same real-PostgreSQL-via-EnsureCreatedAsync pattern as
/// RulesDbContext (Day 31) and IdentityDbContext (Day 23) - no EF Core
/// Migrations pipeline exists in this codebase yet.
/// </summary>
public sealed class ConfigDbContext : PlatformDbContext
{
    public ConfigDbContext(DbContextOptions<ConfigDbContext> options) : base(options)
    {
    }

    public DbSet<ConfigEntry> ConfigEntries => Set<ConfigEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ConfigEntry>(entry =>
        {
            entry.HasIndex(e => e.Key).IsUnique();
            entry.Property(e => e.Key).HasMaxLength(200);
            entry.Property(e => e.Category).HasMaxLength(100);
            entry.Property(e => e.ValueType).HasConversion<string>();
        });
    }
}
