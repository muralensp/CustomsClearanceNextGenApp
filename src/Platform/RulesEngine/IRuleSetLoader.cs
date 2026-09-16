namespace CustomsClearance.Platform.RulesEngine;

/// <summary>Loads the Rules applicable to a set of pipeline-stage categories on a given evaluation date (ADR-0013).</summary>
public interface IRuleSetLoader
{
    Task<IReadOnlyList<ApplicableRule>> LoadAsync(RuleCategory[] categories, DateOnly evaluationDate, CancellationToken cancellationToken = default);
}
