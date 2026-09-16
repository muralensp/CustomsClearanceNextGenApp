using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Platform.RulesEngine;

/// <summary>
/// Real EF Core implementation of IRuleSetLoader (Day 32), against the
/// Day 31 schema. Query shape per ADR-0013: Rule.Active == true AND
/// Rule.Category IN categories AND RuleVersion.EffectiveFrom &lt;=
/// evaluationDate AND (RuleVersion.EffectiveTo IS NULL OR EffectiveTo &gt;=
/// evaluationDate). Exactly one RuleVersion per Rule is expected to
/// satisfy this - a data invariant enforced by the Day 34 admin API
/// (ADR-0013/ADR-0025), not re-checked here; SingleOrDefault would throw on
/// a violation rather than silently pick one, surfacing a real authoring
/// bug instead of masking it.
/// </summary>
public sealed class EfRuleSetLoader : IRuleSetLoader
{
    private readonly RulesDbContext _dbContext;

    public EfRuleSetLoader(RulesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ApplicableRule>> LoadAsync(RuleCategory[] categories, DateOnly evaluationDate, CancellationToken cancellationToken = default)
    {
        var rules = await _dbContext.Rules
            .Where(r => r.Active && categories.Contains(r.Category))
            .Include(r => r.Versions)
            .ThenInclude(v => v.Conditions)
            .ToListAsync(cancellationToken);

        var applicable = new List<ApplicableRule>();
        foreach (var rule in rules)
        {
            var version = rule.Versions.SingleOrDefault(v =>
                v.EffectiveFrom <= evaluationDate &&
                (v.EffectiveTo is null || v.EffectiveTo >= evaluationDate));

            if (version is not null)
            {
                applicable.Add(new ApplicableRule(rule, version, version.Conditions.ToList()));
            }
        }

        return applicable;
    }
}
