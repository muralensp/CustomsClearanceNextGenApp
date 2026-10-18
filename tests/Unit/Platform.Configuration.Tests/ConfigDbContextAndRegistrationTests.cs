using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CustomsClearance.Platform.Configuration.Tests;

public class ConfigDbContextAndRegistrationTests
{
    [Fact]
    public async Task SaveChanges_StampsCreatedAtAndUpdatedAt()
    {
        using var db = new ConfigDbContext(new DbContextOptionsBuilder<ConfigDbContext>()
            .UseInMemoryDatabase(nameof(SaveChanges_StampsCreatedAtAndUpdatedAt)).Options);
        var entry = new ConfigEntry { Id = Guid.NewGuid(), Key = "k", Value = "v" };
        db.ConfigEntries.Add(entry);
        await db.SaveChangesAsync();

        Assert.NotEqual(default, entry.CreatedAt);
        Assert.Null(entry.UpdatedAt);

        entry.Value = "v2";
        await db.SaveChangesAsync();

        Assert.NotNull(entry.UpdatedAt);
    }

    [Fact]
    public void Model_DeclaresUniqueIndexOnKey()
    {
        using var db = new ConfigDbContext(new DbContextOptionsBuilder<ConfigDbContext>()
            .UseInMemoryDatabase(nameof(Model_DeclaresUniqueIndexOnKey)).Options);

        var entityType = db.Model.FindEntityType(typeof(ConfigEntry))!;
        var index = entityType.GetIndexes().Single(i => i.Properties.Single().Name == nameof(ConfigEntry.Key));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public async Task AddCustomsClearanceConfiguration_ResolvesConfigService()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ConfigDbContext>(o => o.UseInMemoryDatabase(nameof(AddCustomsClearanceConfiguration_ResolvesConfigService)));
        services.AddCustomsClearanceConfiguration(TimeSpan.FromSeconds(5));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<ConfigService>(scope.ServiceProvider.GetRequiredService<IConfigService>());
        Assert.Equal(TimeSpan.FromSeconds(5), provider.GetRequiredService<ConfigCacheOptions>().Ttl);
    }

    [Fact]
    public void AddCustomsClearanceConfiguration_DefaultsTtlTo30Seconds()
    {
        var services = new ServiceCollection();
        services.AddCustomsClearanceConfiguration();

        using var provider = services.BuildServiceProvider();

        Assert.Equal(TimeSpan.FromSeconds(30), provider.GetRequiredService<ConfigCacheOptions>().Ttl);
    }
}
