using CustomsClearance.Platform.Persistence;

namespace CustomsClearance.Platform.RulesEngine;

/// <summary>
/// One effective-dated, immutable-once-published version of a
/// <see cref="Rule"/> - the actual thing a Day 32 evaluation matches
/// against (per ADR-0013's IRuleSetLoader query shape:
/// "RuleVersion.EffectiveFrom &lt;= evaluationDate AND (EffectiveTo IS NULL
/// OR EffectiveTo &gt;= evaluationDate)"). Exactly one RuleVersion per Rule
/// may be effective on any given date - a data invariant enforced by the
/// Day 34 admin API, not by this schema (ADR-0013's own scoping note).
///
/// The "action" the Day-Wise Plan's Day 31 wording names: firing this
/// version sets Facts[OutcomeFactName] = OutcomeValue (ADR-0013's
/// ApplyOutcome, which mutates Facts in place so later pipeline stages see
/// this stage's result - e.g. an HS Code rule setting
/// "ControlledGoodsFlag" = "true" for a later Permit rule to read).
/// Generalized beyond the Day 19 spike's clearance-type-only
/// SetsClearanceType field, since PRD Ch.5.7's later stages (Permit,
/// Warning) need to react to flags a rule sets, not just a clearance type.
/// </summary>
public sealed class RuleVersion : AuditableEntity<Guid>
{
    public required Guid RuleId { get; set; }

    public Rule? Rule { get; set; }

    /// <summary>1-based, increasing per Rule - the "version" the Day-Wise Plan names explicitly (distinct from EF Core's own row versioning/concurrency, which this schema does not yet use).</summary>
    public required int VersionNumber { get; set; }

    public required DateOnly EffectiveFrom { get; set; }

    /// <summary>Null = open-ended (still current).</summary>
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>The Facts key this version's outcome sets when it wins its pipeline stage (e.g. "ClearanceType", "ControlledGoodsFlag").</summary>
    public required string OutcomeFactName { get; set; }

    /// <summary>The value assigned to OutcomeFactName (e.g. "FML", "true") - stored as text; Day 32's evaluator is responsible for any typed parsing, mirroring RuleCondition.Value below.</summary>
    public required string OutcomeValue { get; set; }

    public ICollection<RuleCondition> Conditions { get; } = new List<RuleCondition>();
}
