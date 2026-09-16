using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CustomsClearance.Platform.RulesEngine.Tests;

/// <summary>
/// EF Core InMemory provider - fast, no infrastructure required, per
/// ADR-0009's "unit tests should not require infrastructure" principle.
/// Real PostgreSQL connectivity (schema actually migrates, unique indexes
/// actually enforced) is proven separately via a live docker-compose run
/// (Day 31 Execution Log), the same split Platform.Persistence's own tests
/// already establish.
/// </summary>
public class RulesDbContextTests
{
    private static RulesDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<RulesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new RulesDbContext(options);
    }

    private static Rule NewRule(string code = "DEMINIMIS-GIFT-001") => new()
    {
        Id = Guid.NewGuid(),
        RuleCode = code,
        Name = "India De Minimis - Gift Import",
        Description = "Gift imports under threshold clear as De Minimis.",
        Category = RuleCategory.DeMinimis,
        Priority = 5,
    };

    private static RuleVersion NewVersion(Guid ruleId) => new()
    {
        Id = Guid.NewGuid(),
        RuleId = ruleId,
        VersionNumber = 1,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        EffectiveTo = null,
        OutcomeFactName = "ClearanceType",
        OutcomeValue = "DEM",
    };

    private static RuleCondition NewCondition(Guid ruleVersionId) => new()
    {
        Id = Guid.NewGuid(),
        RuleVersionId = ruleVersionId,
        FactName = "ShipmentValue",
        Operator = ConditionOperator.LessThan,
        Value = "5000",
    };

    [Fact]
    public async Task FullGraph_PersistsAndReloadsWithNavigations()
    {
        var options = new DbContextOptionsBuilder<RulesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var rule = NewRule();
        var version = NewVersion(rule.Id);
        var condition = NewCondition(version.Id);

        await using (var writeContext = new RulesDbContext(options))
        {
            await writeContext.Rules.AddAsync(rule);
            await writeContext.RuleVersions.AddAsync(version);
            await writeContext.RuleConditions.AddAsync(condition);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = new RulesDbContext(options);
        var reloadedRule = await readContext.Rules
            .Include(r => r.Versions)
            .ThenInclude(v => v.Conditions)
            .SingleAsync(r => r.Id == rule.Id);

        Assert.Equal("DEMINIMIS-GIFT-001", reloadedRule.RuleCode);
        Assert.Equal(RuleCategory.DeMinimis, reloadedRule.Category);
        Assert.Equal(5, reloadedRule.Priority);
        Assert.True(reloadedRule.Active);
        Assert.NotEqual(default, reloadedRule.CreatedAt);

        var reloadedVersion = Assert.Single(reloadedRule.Versions);
        Assert.Equal(1, reloadedVersion.VersionNumber);
        Assert.Equal(new DateOnly(2026, 1, 1), reloadedVersion.EffectiveFrom);
        Assert.Null(reloadedVersion.EffectiveTo);
        Assert.Equal("ClearanceType", reloadedVersion.OutcomeFactName);
        Assert.Equal("DEM", reloadedVersion.OutcomeValue);

        var reloadedCondition = Assert.Single(reloadedVersion.Conditions);
        Assert.Equal("ShipmentValue", reloadedCondition.FactName);
        Assert.Equal(ConditionOperator.LessThan, reloadedCondition.Operator);
        Assert.Equal("5000", reloadedCondition.Value);
    }

    [Fact]
    public async Task RemovingRule_CascadesToVersionsAndConditions()
    {
        var options = new DbContextOptionsBuilder<RulesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var rule = NewRule();
        var version = NewVersion(rule.Id);
        var condition = NewCondition(version.Id);

        await using (var writeContext = new RulesDbContext(options))
        {
            await writeContext.Rules.AddAsync(rule);
            await writeContext.RuleVersions.AddAsync(version);
            await writeContext.RuleConditions.AddAsync(condition);
            await writeContext.SaveChangesAsync();
        }

        await using (var deleteContext = new RulesDbContext(options))
        {
            var loaded = await deleteContext.Rules
                .Include(r => r.Versions)
                .ThenInclude(v => v.Conditions)
                .SingleAsync(r => r.Id == rule.Id);
            deleteContext.Rules.Remove(loaded);
            await deleteContext.SaveChangesAsync();
        }

        await using var verifyContext = new RulesDbContext(options);
        Assert.False(await verifyContext.Rules.AnyAsync(r => r.Id == rule.Id));
        Assert.False(await verifyContext.RuleVersions.AnyAsync(v => v.Id == version.Id));
        Assert.False(await verifyContext.RuleConditions.AnyAsync(c => c.Id == condition.Id));
    }

    [Fact]
    public async Task RuleRepository_GetByIdAsync_ReturnsSavedRule()
    {
        await using var context = CreateContext();
        var repository = new RuleRepository(context);
        var rule = NewRule("HSCODE-CONTROLLED-001");

        await repository.AddAsync(rule);
        await context.SaveChangesAsync();
        var found = await repository.GetByIdAsync(rule.Id);

        Assert.NotNull(found);
        Assert.Equal("HSCODE-CONTROLLED-001", found!.RuleCode);
    }

    [Fact]
    public async Task RuleVersionRepository_And_RuleConditionRepository_RoundTrip()
    {
        await using var context = CreateContext();
        var rule = NewRule();
        await context.Rules.AddAsync(rule);
        await context.SaveChangesAsync();

        var versionRepository = new RuleVersionRepository(context);
        var version = NewVersion(rule.Id);
        await versionRepository.AddAsync(version);
        await context.SaveChangesAsync();

        var conditionRepository = new RuleConditionRepository(context);
        var condition = NewCondition(version.Id);
        await conditionRepository.AddAsync(condition);
        await context.SaveChangesAsync();

        Assert.NotNull(await versionRepository.GetByIdAsync(version.Id));
        Assert.NotNull(await conditionRepository.GetByIdAsync(condition.Id));
        Assert.Single(await versionRepository.GetAllAsync());
        Assert.Single(await conditionRepository.GetAllAsync());
    }
}
