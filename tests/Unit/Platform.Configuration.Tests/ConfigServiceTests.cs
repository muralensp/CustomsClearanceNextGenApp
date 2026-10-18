using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CustomsClearance.Platform.Configuration.Tests;

public class ConfigServiceTests
{
    private static ConfigDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<ConfigDbContext>().UseInMemoryDatabase(name).Options);

    private static ConfigService NewService(ConfigDbContext db, IMemoryCache cache, TimeSpan? ttl = null) =>
        new(db, cache, new ConfigCacheOptions(ttl ?? TimeSpan.FromMinutes(5)));

    private static async Task SeedAsync(ConfigDbContext db, string key, string value, ConfigValueType type = ConfigValueType.String)
    {
        db.ConfigEntries.Add(new ConfigEntry { Id = Guid.NewGuid(), Key = key, Value = value, ValueType = type });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task TypedGetters_ParseStoredText()
    {
        using var db = NewDb(nameof(TypedGetters_ParseStoredText));
        await SeedAsync(db, "s", "hello");
        await SeedAsync(db, "i", "5000", ConfigValueType.Integer);
        await SeedAsync(db, "d", "12.5", ConfigValueType.Decimal);
        await SeedAsync(db, "b", "true", ConfigValueType.Boolean);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = NewService(db, cache);

        Assert.Equal("hello", await service.GetStringAsync("s"));
        Assert.Equal(5000L, await service.GetIntegerAsync("i"));
        Assert.Equal(12.5m, await service.GetDecimalAsync("d"));
        Assert.True(await service.GetBooleanAsync("b"));
    }

    [Fact]
    public async Task TypedGetters_ReturnNullWhenMissingOrNotParseable()
    {
        using var db = NewDb(nameof(TypedGetters_ReturnNullWhenMissingOrNotParseable));
        await SeedAsync(db, "s", "hello");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = NewService(db, cache);

        Assert.Null(await service.GetStringAsync("missing"));
        Assert.Null(await service.GetIntegerAsync("s"));
        Assert.Null(await service.GetDecimalAsync("s"));
        Assert.Null(await service.GetBooleanAsync("s"));
    }

    [Fact]
    public async Task IsEnabled_TrueOnlyForTrueFlag_MissingFlagIsOff()
    {
        using var db = NewDb(nameof(IsEnabled_TrueOnlyForTrueFlag_MissingFlagIsOff));
        await SeedAsync(db, "on", "true", ConfigValueType.Boolean);
        await SeedAsync(db, "off", "false", ConfigValueType.Boolean);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = NewService(db, cache);

        Assert.True(await service.IsEnabledAsync("on"));
        Assert.False(await service.IsEnabledAsync("off"));
        Assert.False(await service.IsEnabledAsync("missing"));
    }

    [Fact]
    public async Task Read_IsServedFromCacheUntilInvalidated()
    {
        using var db = NewDb(nameof(Read_IsServedFromCacheUntilInvalidated));
        await SeedAsync(db, "k", "old");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = NewService(db, cache);

        Assert.Equal("old", await service.GetStringAsync("k"));

        var entry = await db.ConfigEntries.SingleAsync(e => e.Key == "k");
        entry.Value = "new";
        await db.SaveChangesAsync();

        Assert.Equal("old", await service.GetStringAsync("k"));

        service.Invalidate("k");
        Assert.Equal("new", await service.GetStringAsync("k"));
    }

    [Fact]
    public async Task MissingKey_IsNegativelyCached_UntilInvalidated()
    {
        using var db = NewDb(nameof(MissingKey_IsNegativelyCached_UntilInvalidated));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = NewService(db, cache);

        Assert.Null(await service.GetStringAsync("late"));

        await SeedAsync(db, "late", "now-present");
        Assert.Null(await service.GetStringAsync("late"));

        service.Invalidate("late");
        Assert.Equal("now-present", await service.GetStringAsync("late"));
    }

    [Fact]
    public async Task Read_PicksUpChangeAfterTtlExpires()
    {
        using var db = NewDb(nameof(Read_PicksUpChangeAfterTtlExpires));
        await SeedAsync(db, "k", "old");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = NewService(db, cache, TimeSpan.FromMilliseconds(50));

        Assert.Equal("old", await service.GetStringAsync("k"));

        var entry = await db.ConfigEntries.SingleAsync(e => e.Key == "k");
        entry.Value = "new";
        await db.SaveChangesAsync();

        await Task.Delay(300);
        Assert.Equal("new", await service.GetStringAsync("k"));
    }
}
