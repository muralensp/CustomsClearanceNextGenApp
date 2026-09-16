namespace CustomsClearance.Platform.RulesEngine;

/// <summary>One PRD Ch.5.7 pipeline stage's result - even a non-match ("no match"), so absence-of-match is itself traceable (PRD Ch.5.13).</summary>
public sealed record DecisionTraceStep(int StepOrder, string StepName, string? RuleCode, string Outcome, IReadOnlyDictionary<string, object?> FactsSnapshot);

/// <summary>
/// Captures one DecisionTraceStep per pipeline stage as RulesEngine
/// evaluates it (ADR-0013). Day 32 scope is evaluation only - a real,
/// persisted DecisionTrace entity/table is Day 33's explicit scope
/// ("Rules Engine - Versioning & Decision Trace"), per ADR-0013's own
/// Days 31-35 breakdown. InMemoryDecisionTraceRecorder (below) is the
/// Day 32 implementation, good enough to prove the pipeline end-to-end;
/// Day 33 adds an EF-backed implementation without changing this
/// interface or RulesEngine's orchestration.
/// </summary>
public interface IDecisionTraceRecorder
{
    void RecordStep(ApplicableRule? winningRule, Facts facts, int stepOrder, string stepName);

    IReadOnlyList<DecisionTraceStep> Flush();
}

public sealed class InMemoryDecisionTraceRecorder : IDecisionTraceRecorder
{
    private readonly List<DecisionTraceStep> _steps = [];

    public void RecordStep(ApplicableRule? winningRule, Facts facts, int stepOrder, string stepName) =>
        _steps.Add(new DecisionTraceStep(
            stepOrder,
            stepName,
            winningRule?.Rule.RuleCode,
            winningRule is null ? "no match" : $"{winningRule.Version.OutcomeFactName} = {winningRule.Version.OutcomeValue}",
            facts.Snapshot()));

    public IReadOnlyList<DecisionTraceStep> Flush() => _steps;
}
