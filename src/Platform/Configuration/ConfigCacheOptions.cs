namespace CustomsClearance.Platform.Configuration;

public sealed record ConfigCacheOptions(TimeSpan Ttl)
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);
}
