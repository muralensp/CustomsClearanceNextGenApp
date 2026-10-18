using CustomsClearance.Platform.Persistence;

namespace CustomsClearance.Platform.ReferenceData;

/// <summary>
/// Shared shape of a reference/lookup row. Code is the business key
/// (unique per table). Source and IsVerified record provenance, so
/// placeholder rows can never be mistaken for sourced, production data.
/// </summary>
public abstract class ReferenceEntity : AuditableEntity<Guid>
{
    public required string Code { get; set; }

    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;

    public required string Source { get; set; }

    public bool IsVerified { get; set; }
}

/// <summary>Code is the ISO 3166-1 alpha-2 code.</summary>
public sealed class Country : ReferenceEntity
{
    public required string Alpha3Code { get; set; }
}

/// <summary>Code is the ISO 4217 alphabetic code.</summary>
public sealed class Currency : ReferenceEntity
{
    public required string NumericCode { get; set; }

    public int MinorUnits { get; set; }
}

/// <summary>Code is the unit quantity code (UQC) used on Indian customs declarations.</summary>
public sealed class UnitOfMeasure : ReferenceEntity
{
}
