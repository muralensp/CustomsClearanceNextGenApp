using CustomsClearance.Platform.Persistence;

namespace CustomsClearance.Platform.RulesEngine;

/// <summary>
/// One persisted PRD Ch.5.7 pipeline-stage step from one evaluation run
/// (Day 33 - real storage for the trace ADR-0013 designed and Day 32
/// captured only in memory). EvaluationId groups every DecisionTrace row
/// produced by a single RulesEngine.EvaluateAsync call, so "the decision
/// trace for evaluation X" is a single indexed query, not a reconstruction
/// from scattered rows.
///
/// RuleId/RuleVersionId are nullable (a "no match" step records no
/// winner, per PRD Ch.5.13's "absence of match is itself traceable") and
/// deliberately NOT cascade-deleted from Rule/RuleVersion (see
/// RulesDbContext) - a trace row is an audit record (ADR-0004) that must
/// survive even if the Rule/RuleVersion it referenced is later changed;
/// Rules are soft-deactivated, never hard-deleted, per ADR-0025, but this
/// is defense-in-depth for that invariant, not a substitute for it.
///
/// FactsSnapshotJson stores Facts.Snapshot() as JSON text (not a
/// relational shape) - the Facts bag is an open, evaluation-time-variable
/// set of keys (PRD Ch.5.4), so a fixed column-per-fact schema would need
/// a migration every time a new fact name is introduced; PRD Ch.5.13's own
/// requirement is that the trace survive later fact changes, which an
/// immutable JSON blob satisfies directly.
/// </summary>
public sealed class DecisionTrace : AuditableEntity<Guid>
{
    public required Guid EvaluationId { get; set; }

    public required Guid SubjectId { get; set; }

    public required SubjectType SubjectType { get; set; }

    public required DateOnly EvaluationDate { get; set; }

    public required int StepOrder { get; set; }

    public required string StepName { get; set; }

    public Guid? RuleId { get; set; }

    public Guid? RuleVersionId { get; set; }

    /// <summary>"no match", or "{OutcomeFactName} = {OutcomeValue}" for a winning rule - same shape as Day 32's in-memory trace.</summary>
    public required string Outcome { get; set; }

    public required string FactsSnapshotJson { get; set; }
}
