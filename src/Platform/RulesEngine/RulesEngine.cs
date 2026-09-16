namespace CustomsClearance.Platform.RulesEngine;

/// <summary>Final Facts state plus the per-stage trace of how it was reached (ADR-0013).</summary>
public sealed record SegregationResult(Facts Facts, IReadOnlyList<DecisionTraceStep> Trace);

/// <summary>
/// Orchestrates the 5-stage pipeline ADR-0013 designed (Facts assembly ->
/// Rule loading -> Condition matching -> Priority resolution -> Trace
/// capture), against PRD Ch.5.7's fixed stage order (PipelineStages
/// .Ordered). Day 32 scope: the evaluation itself. Facts is supplied by
/// the caller (a real IFactsProvider per SubjectType is later work, once a
/// real bounded context exists to assemble it from - not invented here).
/// </summary>
public sealed class RulesEngine
{
    private readonly IRuleSetLoader _loader;
    private readonly IConditionEvaluator _evaluator;
    private readonly IPriorityResolver _resolver;
    private readonly IDecisionTraceRecorder _tracer;

    public RulesEngine(IRuleSetLoader loader, IConditionEvaluator evaluator, IPriorityResolver resolver, IDecisionTraceRecorder tracer)
    {
        _loader = loader;
        _evaluator = evaluator;
        _resolver = resolver;
        _tracer = tracer;
    }

    public async Task<SegregationResult> EvaluateAsync(EvaluationContext context, Facts facts, CancellationToken cancellationToken = default)
    {
        foreach (var stage in PipelineStages.Ordered)
        {
            var candidates = await _loader.LoadAsync(stage.Categories, context.EvaluationDate, cancellationToken);
            var fired = candidates
                .Where(candidate => candidate.Conditions.All(condition => _evaluator.IsSatisfied(condition, facts)))
                .ToList();

            var winner = _resolver.Resolve(fired);
            _tracer.RecordStep(winner, facts, stage.StepOrder, stage.Name);

            if (winner is not null)
            {
                // ApplyOutcome: mutate Facts in place so later stages see
                // this stage's result (ADR-0013's one deliberate divergence
                // from a frozen-snapshot Facts model - e.g. an HS Code
                // rule's ControlledGoodsFlag needs to be visible to the
                // later Permit stage).
                facts[winner.Version.OutcomeFactName] = winner.Version.OutcomeValue;
            }
        }

        return new SegregationResult(facts, _tracer.Flush());
    }
}
