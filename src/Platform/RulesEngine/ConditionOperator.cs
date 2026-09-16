namespace CustomsClearance.Platform.RulesEngine;

/// <summary>
/// Per ADR-0013's IConditionEvaluator design (Day 3 ER Diagram v0.2 §5.3).
/// The Day 19 spike's ConditionEvaluator only implemented the first four
/// (enough for its 3 scenarios); In/Between are added here in the real
/// schema per the design's full operator set - Day 32's evaluation
/// pipeline is responsible for actually implementing In/Between's list/
/// range parsing of RuleCondition.Value, not this schema.
/// </summary>
public enum ConditionOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    LessThan,

    /// <summary>RuleCondition.Value holds a delimited list (e.g. "IN,PK,BD").</summary>
    In,

    /// <summary>RuleCondition.Value holds a "{min},{max}" pair.</summary>
    Between,
}
