namespace CustomsClearance.Platform.Configuration;

/// <summary>
/// The declared type of a ConfigEntry's string-stored Value. Storage stays a
/// single text column (no provider-specific column types, per ADR-0001's
/// SQL Server migration path); ConfigValueValidator enforces that the text
/// actually parses as the declared type before it is saved.
/// </summary>
public enum ConfigValueType
{
    String,
    Integer,
    Decimal,
    Boolean,
}
