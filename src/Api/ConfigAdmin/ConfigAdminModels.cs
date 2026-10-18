using CustomsClearance.Platform.Configuration;

namespace CustomsClearance.Api.ConfigAdmin;

public sealed record CreateConfigRequest(
    string Key,
    string Value,
    ConfigValueType ValueType,
    string? Category,
    bool IsFeatureFlag,
    string? Description);

public sealed record UpdateConfigRequest(string Value, string? Description);

public sealed record ConfigEntryResponse(
    Guid Id,
    string Key,
    string Value,
    string ValueType,
    string Category,
    bool IsFeatureFlag,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Returned by PUT so the caller (and the log line) carries the old and new
/// value together - the shape Day 38's audit wiring will consume.
/// </summary>
public sealed record ConfigChangeResponse(string Key, string OldValue, string NewValue, ConfigEntryResponse Entry);

/// <summary>Result of reading a key through IConfigService (the cached path).</summary>
public sealed record EffectiveConfigResponse(string Key, string? Value, bool Found);
