namespace CustomsClearance.Platform.RulesEngine;

/// <summary>Picks the single winning rule among those that fired within one pipeline stage (ADR-0013).</summary>
public interface IPriorityResolver
{
    ApplicableRule? Resolve(IReadOnlyList<ApplicableRule> firedRules);
}
