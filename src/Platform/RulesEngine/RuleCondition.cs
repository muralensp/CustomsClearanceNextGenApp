using CustomsClearance.Platform.Persistence;

namespace CustomsClearance.Platform.RulesEngine;

/// <summary>
/// One AND-only condition (per ADR-0002/Day 3-4 resolution: OR is
/// expressed as separate same-priority Rules, not evaluated within a
/// single RuleVersion) a <see cref="RuleVersion"/> must satisfy to fire.
/// Day 32's IConditionEvaluator reads FactName/Operator/Value against the
/// assembled Facts bag; this schema only stores the authored condition.
/// </summary>
public sealed class RuleCondition : AuditableEntity<Guid>
{
    public required Guid RuleVersionId { get; set; }

    public RuleVersion? RuleVersion { get; set; }

    /// <summary>Key into the Facts bag (PRD Ch.5.4 fact names, e.g. "ShipmentValue", "OriginCountry") - an open string, not an enum, per ADR-0013's Facts design.</summary>
    public required string FactName { get; set; }

    public required ConditionOperator Operator { get; set; }

    /// <summary>Scalar for Equals/NotEquals/GreaterThan/LessThan; a delimited list for In; a "{min},{max}" pair for Between - stored as text, parsed by Day 32's IConditionEvaluator per-operator (ADR-0013).</summary>
    public required string Value { get; set; }
}
