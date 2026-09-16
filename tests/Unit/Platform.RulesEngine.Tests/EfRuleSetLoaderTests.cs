using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CustomsClearance.Platform.RulesEngine.Tests;

/// <summary>EF Core InMemory provider (ADR-0009) - proves the real query shape (Active/Category filter, effective-dating, Conditions included), not just the abstraction.</summary>
public class EfRuleSetLoaderTests
{
    private static async Task<RulesDbContext> SeedAsync(params (Rule Rule, RuleVersion Version, RuleCondition[] Conditions)[] seed)
    {
        var options = new DbContextOptionsBuilder<RulesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new RulesDbContext(options);

        foreach (var (rule, version, conditions) in seed)
        {
            await context.Rules.AddAsync(rule);
            await context.RuleVersions.AddAsync(version);
            foreach (var condition in conditions)
            {
                await context.RuleConditions.AddAsync(condition);
            }
        }

        await context.SaveChangesAsync();
        return context;
    }

    private static Rule NewRule(string code, RuleCategory category, int priority, bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        RuleCode = code,
        Name = code,
        Category = category,
        Priority = priority,
        Active = active,
    };

    private static RuleVersion NewVersion(Guid ruleId, DateOnly effectiveFrom, DateOnly? effectiveTo = null) => new()
    {
        Id = Guid.NewGuid(),
        RuleId = ruleId,
        VersionNumber = 1,
        EffectiveFrom = effectiveFrom,
        EffectiveTo = effectiveTo,
        OutcomeFactName = "ClearanceType",
        OutcomeValue = "DEM",
    };

    [Fact]
    public async Task LoadAsync_FiltersByCategory()
    {
        var rule = NewRule("DEMINIMIS-001", RuleCategory.DeMinimis, 5);
        var version = NewVersion(rule.Id, new DateOnly(2026, 1, 1));
        var otherRule = NewRule("HSCODE-001", RuleCategory.HSCode, 5);
        var otherVersion = NewVersion(otherRule.Id, new DateOnly(2026, 1, 1));

        await using var context = await SeedAsync((rule, version, []), (otherRule, otherVersion, []));
        var loader = new EfRuleSetLoader(context);

        var result = await loader.LoadAsync([RuleCategory.DeMinimis], new DateOnly(2026, 6, 1));

        var applicable = Assert.Single(result);
        Assert.Equal("DEMINIMIS-001", applicable.Rule.RuleCode);
    }

    [Fact]
    public async Task LoadAsync_ExcludesInactiveRules()
    {
        var rule = NewRule("INACTIVE-001", RuleCategory.DeMinimis, 5, active: false);
        var version = NewVersion(rule.Id, new DateOnly(2026, 1, 1));

        await using var context = await SeedAsync((rule, version, []));
        var loader = new EfRuleSetLoader(context);

        var result = await loader.LoadAsync([RuleCategory.DeMinimis], new DateOnly(2026, 6, 1));

        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadAsync_ExcludesVersionsNotYetEffective()
    {
        var rule = NewRule("FUTURE-001", RuleCategory.DeMinimis, 5);
        var version = NewVersion(rule.Id, new DateOnly(2027, 1, 1));

        await using var context = await SeedAsync((rule, version, []));
        var loader = new EfRuleSetLoader(context);

        var result = await loader.LoadAsync([RuleCategory.DeMinimis], new DateOnly(2026, 6, 1));

        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadAsync_ExcludesVersionsPastEffectiveTo()
    {
        var rule = NewRule("EXPIRED-001", RuleCategory.DeMinimis, 5);
        var version = NewVersion(rule.Id, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));

        await using var context = await SeedAsync((rule, version, []));
        var loader = new EfRuleSetLoader(context);

        var result = await loader.LoadAsync([RuleCategory.DeMinimis], new DateOnly(2026, 6, 1));

        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadAsync_IncludesOpenEndedVersion()
    {
        var rule = NewRule("OPEN-001", RuleCategory.DeMinimis, 5);
        var version = NewVersion(rule.Id, new DateOnly(2025, 1, 1), effectiveTo: null);

        await using var context = await SeedAsync((rule, version, []));
        var loader = new EfRuleSetLoader(context);

        var result = await loader.LoadAsync([RuleCategory.DeMinimis], new DateOnly(2030, 1, 1));

        Assert.Single(result);
    }

    [Fact]
    public async Task LoadAsync_IncludesConditions()
    {
        var rule = NewRule("WITHCOND-001", RuleCategory.DeMinimis, 5);
        var version = NewVersion(rule.Id, new DateOnly(2026, 1, 1));
        var condition = new RuleCondition
        {
            Id = Guid.NewGuid(),
            RuleVersionId = version.Id,
            FactName = "ShipmentValue",
            Operator = ConditionOperator.LessThan,
            Value = "5000",
        };

        await using var context = await SeedAsync((rule, version, [condition]));
        var loader = new EfRuleSetLoader(context);

        var result = await loader.LoadAsync([RuleCategory.DeMinimis], new DateOnly(2026, 6, 1));

        var applicable = Assert.Single(result);
        var loadedCondition = Assert.Single(applicable.Conditions);
        Assert.Equal("ShipmentValue", loadedCondition.FactName);
    }
}
