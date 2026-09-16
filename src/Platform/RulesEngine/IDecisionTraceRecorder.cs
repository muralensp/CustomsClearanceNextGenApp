namespace CustomsClearance.Platform.RulesEngine;

/// <summary>One PRD Ch.5.7 pipeline stage's result - even a non-match ("no match"), so absence-of-match is itself traceable (PRD Ch.5.13).</summary>
public sealed record DecisionTraceStep(Guid EvaluationId, int StepOrder, string StepName, string? RuleCode, string Outcome, IReadOnlyDictionary<string, object?> FactsSnapshot);

/// <summary>
/// Captures one DecisionTraceStep per pipeline stage as RulesEngine
/// evaluates it (ADR-0013). FlushAsync is async per ADR-0013's original
/// design (the Day 32 in-memory implementation used a sync Flush() since
/// it didn't need I/O yet - corrected here to match the spec now that
/// EfDecisionTraceRecorder needs a real SaveChangesAsync).
/// InMemoryDecisionTraceRecorder (below) remains useful for tests that
/// don't need persistence (RulesEngineTests); EfDecisionTraceRecorder
/// (Day 33) is the real, persisted implementation registered in DI.
/// </summary>
public interface IDecisionTraceRecorder
{
    void RecordStep(Guid evaluationId, EvaluationContext context, ApplicableRule? winningRule, Facts facts, int stepOrder, string stepName);

    Task<IReadOnlyList<DecisionTraceStep>> FlushAsync(CancellationToken cancellationToken = default);
}

public sealed class InMemoryDecisionTraceRecorder : IDecisionTraceRecorder
{
    private readonly List<DecisionTraceStep> _steps = [];

    public void RecordStep(Guid evaluationId, EvaluationContext context, ApplicableRule? winningRule, Facts facts, int stepOrder, string stepName) =>
        _steps.Add(new DecisionTraceStep(
            evaluationId,
            stepOrder,
            stepName,
            winningRule?.Rule.RuleCode,
            winningRule is null ? "no match" : $"{winningRule.Version.OutcomeFactName} = {winningRule.Version.OutcomeValue}",
            facts.Snapshot()));

    public Task<IReadOnlyList<DecisionTraceStep>> FlushAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DecisionTraceStep>>(_steps);
}
