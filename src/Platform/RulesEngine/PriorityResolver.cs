namespace CustomsClearance.Platform.RulesEngine;

/// <summary>
/// PRD Ch.5.9: lowest Priority number wins (1 = Legal Restrictions is
/// highest). Ties broken deterministically by RuleCode (ordinal) per
/// ADR-0013 - a data-authoring conflict surfaced as a repeatable, not
/// crashing, outcome (PRD Ch.5.14's determinism requirement), not a
/// structurally-designed-for case (OR-logic is meant to be expressed as
/// multiple same-priority rules, so exact ties are expected to be rare).
/// </summary>
public sealed class PriorityResolver : IPriorityResolver
{
    public ApplicableRule? Resolve(IReadOnlyList<ApplicableRule> firedRules)
    {
        if (firedRules.Count == 0)
        {
            return null;
        }

        return firedRules
            .OrderBy(r => r.Rule.Priority)
            .ThenBy(r => r.Rule.RuleCode, StringComparer.Ordinal)
            .First();
    }
}
