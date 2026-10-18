using System.Globalization;

namespace CustomsClearance.Platform.Configuration;

/// <summary>
/// Pure check that a text Value really is the ConfigValueType it claims.
/// Invariant culture throughout - "1,5" must never mean 1.5 on one host and
/// 15 on another.
/// </summary>
public static class ConfigValueValidator
{
    public static bool IsValid(ConfigValueType type, string? value)
    {
        if (value is null)
        {
            return false;
        }

        return type switch
        {
            ConfigValueType.String => true,
            ConfigValueType.Integer => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            ConfigValueType.Decimal => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            ConfigValueType.Boolean => bool.TryParse(value, out _),
            _ => false,
        };
    }
}
