using CustomsClearance.Platform.Persistence;

namespace CustomsClearance.Platform.Configuration;

/// <summary>
/// One runtime-changeable configuration value or feature flag. Key is the
/// unique, dotted business identifier (e.g. "currency.base",
/// "feature.new-duty-calc"); Value is always stored as text and interpreted
/// per ValueType.
/// </summary>
public sealed class ConfigEntry : AuditableEntity<Guid>
{
    public required string Key { get; set; }

    public required string Value { get; set; }

    public ConfigValueType ValueType { get; set; }

    public string Category { get; set; } = "General";

    public bool IsFeatureFlag { get; set; }

    public string? Description { get; set; }
}
