using System.Text.Json;

namespace CustomsClearance.Platform.RulesEngine;

/// <summary>
/// Real, persisted IDecisionTraceRecorder (Day 33) - buffers one
/// DecisionTrace entity per RecordStep call into RulesDbContext's change
/// tracker, then writes them all in one SaveChangesAsync on FlushAsync
/// (one round trip per evaluation, not one per step). Scoped in DI (see
/// ServiceCollectionExtensions), so one recorder instance - and one
/// FactsSnapshotJson serializer - lives for exactly one
/// RulesEngine.EvaluateAsync call.
/// </summary>
public sealed class EfDecisionTraceRecorder : IDecisionTraceRecorder
{
    private readonly RulesDbContext _dbContext;
    private readonly List<DecisionTraceStep> _steps = [];

    public EfDecisionTraceRecorder(RulesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void RecordStep(Guid evaluationId, EvaluationContext context, ApplicableRule? winningRule, Facts facts, int stepOrder, string stepName)
    {
        var outcome = winningRule is null
            ? "no match"
            : $"{winningRule.Version.OutcomeFactName} = {winningRule.Version.OutcomeValue}";

        var trace = new DecisionTrace
        {
            Id = Guid.NewGuid(),
            EvaluationId = evaluationId,
            SubjectId = context.SubjectId,
            SubjectType = context.SubjectType,
            EvaluationDate = context.EvaluationDate,
            StepOrder = stepOrder,
            StepName = stepName,
            RuleId = winningRule?.Rule.Id,
            RuleVersionId = winningRule?.Version.Id,
            Outcome = outcome,
            FactsSnapshotJson = JsonSerializer.Serialize(facts.Snapshot()),
        };

        _dbContext.DecisionTraces.Add(trace);
        _steps.Add(new DecisionTraceStep(evaluationId, stepOrder, stepName, winningRule?.Rule.RuleCode, outcome, facts.Snapshot()));
    }

    public async Task<IReadOnlyList<DecisionTraceStep>> FlushAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
        return _steps;
    }
}
