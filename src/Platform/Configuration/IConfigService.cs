namespace CustomsClearance.Platform.Configuration;

/// <summary>
/// Read side of the configuration service. Reads go through a short
/// in-memory cache (ADR-0030); writers call Invalidate so a change is
/// visible on this host immediately rather than after the TTL.
/// </summary>
public interface IConfigService
{
    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);

    Task<long?> GetIntegerAsync(string key, CancellationToken cancellationToken = default);

    Task<decimal?> GetDecimalAsync(string key, CancellationToken cancellationToken = default);

    Task<bool?> GetBooleanAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>True only when the key exists and its value parses as true; a missing flag is off.</summary>
    Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default);

    void Invalidate(string key);
}
