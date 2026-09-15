namespace RulesEngineSpike;

// Throwaway Day 19 spike: validates the ADR-0013 pipeline shape (Facts assembly -> Rule loading ->
// Condition matching -> Priority resolution -> Trace capture) against PRD Ch.5.10 decision-table
// scenarios. In-memory only, no Platform.Persistence/EF dependency, no admin authoring — those are
// Phase 0 Days 31-35 per ADR-0013's "Explicitly out of scope" section.

public enum RuleCategory { Country, Customer, Shipment, DeMinimis, Commodity, HSCode, Duty, Permit, Warning }

public sealed record PipelineStage(int StepOrder, string Name, RuleCategory[] Categories);

public static class PipelineStages
{
    // PRD Ch.5.7 fixed order, hardcoded per ADR-0013 (stage order is a product decision, not
    // rule-authoring policy). Trimmed to the categories the 3 spike scenarios actually exercise.
    public static readonly PipelineStage[] Ordered =
    [
        new(1, "Country Rule", [RuleCategory.Country]),
        new(2, "FTZ Check", [RuleCategory.Shipment]),
        new(3, "HS Code Rule", [RuleCategory.HSCode]),
        new(4, "Permit Rule", [RuleCategory.Permit]),
        new(5, "Duty Rule", [RuleCategory.Duty]),
        new(6, "De Minimis Rule", [RuleCategory.DeMinimis]),
        new(7, "Customer Rule", [RuleCategory.Customer]),
        new(8, "Final Decision", [RuleCategory.Warning]),
    ];
}

public sealed class Facts
{
    private readonly Dictionary<string, object?> _values = new();
    public object? this[string factName]
    {
        get => _values.GetValueOrDefault(factName);
        set => _values[factName] = value;
    }
    public IReadOnlyDictionary<string, object?> Snapshot() => new Dictionary<string, object?>(_values);
}

public enum ConditionOperator { Equals, NotEquals, GreaterThan, LessThan }

public sealed record RuleCondition(string FactName, ConditionOperator Operator, object Value);

public sealed record Rule(string RuleId, RuleCategory Category, int Priority, string SetsClearanceType,
    IReadOnlyList<RuleCondition> Conditions);

public sealed record ApplicableRule(Rule Rule);

public interface IFactsProvider
{
    Facts Assemble();
}

public interface IRuleSetLoader
{
    IReadOnlyList<ApplicableRule> Load(RuleCategory[] categories);
}

public interface IConditionEvaluator
{
    bool IsSatisfied(RuleCondition condition, Facts facts);
}

public interface IPriorityResolver
{
    ApplicableRule? Resolve(IReadOnlyList<ApplicableRule> firedRules);
}

public sealed record DecisionTrace(int StepOrder, string StepName, string? RuleId, string Outcome,
    IReadOnlyDictionary<string, object?> FactsSnapshot);

public interface IDecisionTraceRecorder
{
    void RecordStep(int stepOrder, string stepName, ApplicableRule? winningRule, Facts facts);
    IReadOnlyList<DecisionTrace> Flush();
}

public sealed class ConditionEvaluator : IConditionEvaluator
{
    public bool IsSatisfied(RuleCondition condition, Facts facts)
    {
        var actual = facts[condition.FactName];
        if (actual is null) return false;

        return condition.Operator switch
        {
            ConditionOperator.Equals => Equals(actual, condition.Value),
            ConditionOperator.NotEquals => !Equals(actual, condition.Value),
            ConditionOperator.GreaterThan => Comparer<object>.Default.Compare(actual, condition.Value) > 0,
            ConditionOperator.LessThan => Comparer<object>.Default.Compare(actual, condition.Value) < 0,
            _ => throw new NotSupportedException($"Operator {condition.Operator} not supported in spike."),
        };
    }
}

public sealed class PriorityResolver : IPriorityResolver
{
    public ApplicableRule? Resolve(IReadOnlyList<ApplicableRule> firedRules)
    {
        if (firedRules.Count == 0) return null;

        // Lowest Priority number wins (PRD Ch.5.9: 1=Legal Restrictions is highest). Ties broken
        // deterministically by RuleId per ADR-0013 (data-authoring conflict, not a runtime crash).
        return firedRules
            .OrderBy(r => r.Rule.Priority)
            .ThenBy(r => r.Rule.RuleId, StringComparer.Ordinal)
            .First();
    }
}

public sealed class InMemoryRuleSetLoader(IReadOnlyList<Rule> rules) : IRuleSetLoader
{
    public IReadOnlyList<ApplicableRule> Load(RuleCategory[] categories) =>
        rules.Where(r => categories.Contains(r.Category)).Select(r => new ApplicableRule(r)).ToList();
}

public sealed class InMemoryDecisionTraceRecorder : IDecisionTraceRecorder
{
    private readonly List<DecisionTrace> _steps = [];

    public void RecordStep(int stepOrder, string stepName, ApplicableRule? winningRule, Facts facts) =>
        _steps.Add(new DecisionTrace(
            stepOrder,
            stepName,
            winningRule?.Rule.RuleId,
            winningRule is null ? "no match" : $"CT = {winningRule.Rule.SetsClearanceType}",
            facts.Snapshot()));

    public IReadOnlyList<DecisionTrace> Flush() => _steps;
}

public sealed record SegregationResult(string? ClearanceType, IReadOnlyList<DecisionTrace> Traces);

public sealed class RulesEngine(
    IRuleSetLoader loader,
    IConditionEvaluator evaluator,
    IPriorityResolver resolver,
    IDecisionTraceRecorder tracer)
{
    public SegregationResult Evaluate(Facts facts)
    {
        string? clearanceType = null;

        foreach (var stage in PipelineStages.Ordered)
        {
            var candidates = loader.Load(stage.Categories);
            var fired = candidates
                .Where(c => c.Rule.Conditions.All(cond => evaluator.IsSatisfied(cond, facts)))
                .ToList();
            var winner = resolver.Resolve(fired);
            tracer.RecordStep(stage.StepOrder, stage.Name, winner, facts);

            if (winner is not null)
            {
                // ApplyOutcome: mutate Facts in place so later stages see this stage's result
                // (e.g. HSCode stage's ControlledGoodsFlag feeding the Permit stage) — the one
                // deliberate divergence from a frozen-snapshot Facts model, per ADR-0013.
                clearanceType = winner.Rule.SetsClearanceType;
                facts["ClearanceType"] = clearanceType;
                facts["ClearanceTypeAssigned"] = true;
            }
        }

        return new SegregationResult(clearanceType, tracer.Flush());
    }
}
