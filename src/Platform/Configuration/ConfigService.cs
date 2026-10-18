using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CustomsClearance.Platform.Configuration;

public sealed class ConfigService : IConfigService
{
    private readonly ConfigDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ConfigCacheOptions _options;

    public ConfigService(ConfigDbContext dbContext, IMemoryCache cache, ConfigCacheOptions options)
    {
        _dbContext = dbContext;
        _cache = cache;
        _options = options;
    }

    public Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
        => GetRawAsync(key, cancellationToken);

    public async Task<long?> GetIntegerAsync(string key, CancellationToken cancellationToken = default)
    {
        var raw = await GetRawAsync(key, cancellationToken);
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    public async Task<decimal?> GetDecimalAsync(string key, CancellationToken cancellationToken = default)
    {
        var raw = await GetRawAsync(key, cancellationToken);
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    public async Task<bool?> GetBooleanAsync(string key, CancellationToken cancellationToken = default)
    {
        var raw = await GetRawAsync(key, cancellationToken);
        return bool.TryParse(raw, out var value) ? value : null;
    }

    public async Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default)
        => await GetBooleanAsync(key, cancellationToken) ?? false;

    public void Invalidate(string key) => _cache.Remove(CacheKey(key));

    private async Task<string?> GetRawAsync(string key, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey(key), out CachedValue? cached) && cached is not null)
        {
            return cached.Value;
        }

        var value = await _dbContext.ConfigEntries
            .AsNoTracking()
            .Where(e => e.Key == key)
            .Select(e => e.Value)
            .SingleOrDefaultAsync(cancellationToken);

        // A missing key is cached too (as a null Value) so repeated reads of
        // an absent flag do not each hit the database; creating the key via
        // the admin API calls Invalidate, which clears that negative entry.
        _cache.Set(CacheKey(key), new CachedValue(value), _options.Ttl);
        return value;
    }

    private static string CacheKey(string key) => "config:" + key;

    private sealed record CachedValue(string? Value);
}
