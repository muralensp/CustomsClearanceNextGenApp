namespace CustomsClearance.Platform.RulesEngine;

/// <summary>Stateless single-condition matcher (ADR-0013). A RuleVersion fires only when every one of its conditions is satisfied (AND-only, per ADR-0002).</summary>
public interface IConditionEvaluator
{
    bool IsSatisfied(RuleCondition condition, Facts facts);
}
