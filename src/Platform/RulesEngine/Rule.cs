using CustomsClearance.Platform.Persistence;

namespace CustomsClearance.Platform.RulesEngine;

/// <summary>
/// The top-level, effective-dating-agnostic identity of a business rule
/// (e.g. "India De Minimis - Gift Import"). Per ADR-0002/ADR-0013, a Rule
/// itself carries no conditions or outcome - those live on its
/// <see cref="RuleVersion"/>s, so a policy change (e.g. a CBIC notification
/// raising a threshold) is authored as a new RuleVersion with a new
/// EffectiveFrom date, not an edit to this row or a code release.
/// </summary>
public sealed class Rule : AuditableEntity<Guid>
{
    /// <summary>Stable business identifier (e.g. "DEMINIMIS-GIFT-001"), distinct from Id (Guid PK) - what a compliance admin would reference, per the Day 34 admin API's future URL/lookup key.</summary>
    public required string RuleCode { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Which PRD Ch.5.7 pipeline stage loads this rule (ADR-0013's IRuleSetLoader).</summary>
    public required RuleCategory Category { get; set; }

    /// <summary>PRD Ch.5.9: 1 = Legal Restrictions (highest) ... 9 = Default Rules. Lower number wins on conflict within the same stage.</summary>
    public required int Priority { get; set; }

    /// <summary>Soft-deactivation (per ADR-0013's IRuleSetLoader query: "Rule.Active == true") - rules are never hard-deleted, since past DecisionTrace rows still reference RuleVersions authored under this Rule.</summary>
    public bool Active { get; set; } = true;

    public ICollection<RuleVersion> Versions { get; } = new List<RuleVersion>();
}
