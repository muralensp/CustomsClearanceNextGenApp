using CustomsClearance.Platform.Persistence;

namespace CustomsClearance.Platform.RulesEngine;

/// <summary>
/// Thin EfRepository wrappers (Day 31 schema proof only) - the real
/// query-shaped access Day 32's IRuleSetLoader needs (filter by Category +
/// effective-dating) is that day's scope, not this one's.
///
/// Constructors take the concrete RulesDbContext, NOT the abstract
/// PlatformDbContext EfRepository's own constructor is typed to accept -
/// found for real (Day 31 live verification): the Api host registers only
/// ONE PlatformDbContext DI mapping ("AddScoped&lt;PlatformDbContext&gt;(sp =&gt;
/// sp.GetRequiredService&lt;DiagnosticsDbContext&gt;())", Day 18), so a
/// repository built to accept the base type resolves that same
/// registration and silently tracks entities against DiagnosticsDbContext
/// instead of RulesDbContext - which has no Rule/RuleVersion/RuleCondition
/// DbSets, throwing "Cannot create a DbSet for 'Rule' because this type is
/// not included in the model for the context" at the first insert. Passing
/// the concrete RulesDbContext through to EfRepository's base constructor
/// (accepted there as a PlatformDbContext, since RulesDbContext IS one)
/// sidesteps the single-PlatformDbContext-registration ambiguity entirely.
/// </summary>
public sealed class RuleRepository : EfRepository<Rule, Guid>
{
    public RuleRepository(RulesDbContext dbContext) : base(dbContext)
    {
    }
}

public sealed class RuleVersionRepository : EfRepository<RuleVersion, Guid>
{
    public RuleVersionRepository(RulesDbContext dbContext) : base(dbContext)
    {
    }
}

public sealed class RuleConditionRepository : EfRepository<RuleCondition, Guid>
{
    public RuleConditionRepository(RulesDbContext dbContext) : base(dbContext)
    {
    }
}
