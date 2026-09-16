using Xunit;

namespace CustomsClearance.Platform.RulesEngine.Tests;

public class PriorityResolverTests
{
    private readonly PriorityResolver _resolver = new();

    private static ApplicableRule Rule(string ruleCode, int priority) => new(
        new Rule { Id = Guid.NewGuid(), RuleCode = ruleCode, Name = ruleCode, Category = RuleCategory.Shipment, Priority = priority },
        new RuleVersion { Id = Guid.NewGuid(), RuleId = Guid.NewGuid(), VersionNumber = 1, EffectiveFrom = new DateOnly(2026, 1, 1), OutcomeFactName = "X", OutcomeValue = "Y" },
        []);

    [Fact]
    public void Resolve_NoFiredRules_ReturnsNull()
    {
        Assert.Null(_resolver.Resolve([]));
    }

    [Fact]
    public void Resolve_SingleFiredRule_ReturnsIt()
    {
        var rule = Rule("R1", 5);

        var winner = _resolver.Resolve([rule]);

        Assert.Same(rule, winner);
    }

    [Fact]
    public void Resolve_MultipleFiredRules_LowestPriorityNumberWins()
    {
        var legal = Rule("LEGAL", 1);
        var defaultRule = Rule("DEFAULT", 9);

        var winner = _resolver.Resolve([defaultRule, legal]);

        Assert.Same(legal, winner);
    }

    [Fact]
    public void Resolve_TiedPriority_BreaksTieByRuleCodeOrdinal()
    {
        var ruleB = Rule("RULE-B", 5);
        var ruleA = Rule("RULE-A", 5);

        var winner = _resolver.Resolve([ruleB, ruleA]);

        Assert.Same(ruleA, winner);
    }
}
