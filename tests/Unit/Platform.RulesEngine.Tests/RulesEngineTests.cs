using Xunit;

namespace CustomsClearance.Platform.RulesEngine.Tests;

/// <summary>
/// Day 32 deliverable: "Evaluation service passes unit tests on dummy
/// rules." Uses an in-memory IRuleSetLoader stub (not RulesDbContext) so
/// these tests exercise orchestration logic in isolation from persistence
/// - EfRuleSetLoaderTests already proves the real EF query shape
/// separately.
/// </summary>
public class RulesEngineTests
{
    private sealed class InMemoryRuleSetLoader(IReadOnlyList<ApplicableRule> rules) : IRuleSetLoader
    {
        public Task<IReadOnlyList<ApplicableRule>> LoadAsync(RuleCategory[] categories, DateOnly evaluationDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplicableRule>>(rules.Where(r => categories.Contains(r.Rule.Category)).ToList());
    }

    private static ApplicableRule DummyRule(string ruleCode, RuleCategory category, int priority, string factName, ConditionOperator op, string conditionValue, string outcomeFactName, string outcomeValue)
    {
        var rule = new Rule { Id = Guid.NewGuid(), RuleCode = ruleCode, Name = ruleCode, Category = category, Priority = priority };
        var version = new RuleVersion
        {
            Id = Guid.NewGuid(),
            RuleId = rule.Id,
            VersionNumber = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            OutcomeFactName = outcomeFactName,
            OutcomeValue = outcomeValue,
        };
        var condition = new RuleCondition
        {
            Id = Guid.NewGuid(),
            RuleVersionId = version.Id,
            FactName = factName,
            Operator = op,
            Value = conditionValue,
        };
        return new ApplicableRule(rule, version, [condition]);
    }

    private static RulesEngine BuildEngine(params ApplicableRule[] dummyRules) =>
        new(new InMemoryRuleSetLoader(dummyRules), new ConditionEvaluator(), new PriorityResolver(), new InMemoryDecisionTraceRecorder());

    [Fact]
    public async Task EvaluateAsync_MatchingRule_SetsOutcomeFact()
    {
        var deMinimisRule = DummyRule("DEMINIMIS-001", RuleCategory.DeMinimis, 5, "ShipmentValue", ConditionOperator.LessThan, "5000", "ClearanceType", "DEM");
        var engine = BuildEngine(deMinimisRule);
        var facts = new Facts { ["ShipmentValue"] = 2000m };
        var context = new EvaluationContext(Guid.NewGuid(), SubjectType.Shipment, new DateOnly(2026, 6, 1));

        var result = await engine.EvaluateAsync(context, facts);

        Assert.Equal("DEM", result.Facts["ClearanceType"]);
    }

    [Fact]
    public async Task EvaluateAsync_NonMatchingRule_LeavesFactUnset()
    {
        var deMinimisRule = DummyRule("DEMINIMIS-001", RuleCategory.DeMinimis, 5, "ShipmentValue", ConditionOperator.LessThan, "5000", "ClearanceType", "DEM");
        var engine = BuildEngine(deMinimisRule);
        var facts = new Facts { ["ShipmentValue"] = 999999m };
        var context = new EvaluationContext(Guid.NewGuid(), SubjectType.Shipment, new DateOnly(2026, 6, 1));

        var result = await engine.EvaluateAsync(context, facts);

        Assert.Null(result.Facts["ClearanceType"]);
    }

    [Fact]
    public async Task EvaluateAsync_TwoRulesFireInSameStage_HigherPriorityWins()
    {
        // Both target RuleCategory.DeMinimis (same pipeline stage) and both match.
        var legalRestriction = DummyRule("LEGAL-001", RuleCategory.DeMinimis, 1, "ShipmentValue", ConditionOperator.LessThan, "999999", "ClearanceType", "PROHIBITED");
        var deMinimis = DummyRule("DEMINIMIS-001", RuleCategory.DeMinimis, 5, "ShipmentValue", ConditionOperator.LessThan, "5000", "ClearanceType", "DEM");
        var engine = BuildEngine(deMinimis, legalRestriction);
        var facts = new Facts { ["ShipmentValue"] = 2000m };
        var context = new EvaluationContext(Guid.NewGuid(), SubjectType.Shipment, new DateOnly(2026, 6, 1));

        var result = await engine.EvaluateAsync(context, facts);

        Assert.Equal("PROHIBITED", result.Facts["ClearanceType"]);
    }

    [Fact]
    public async Task EvaluateAsync_LaterStageSeesEarlierStagesOutcome()
    {
        // HS Code stage sets ControlledGoodsFlag; Permit stage's condition
        // reads it - proves Facts mutation is visible across stages
        // (ADR-0013's one deliberate divergence from a frozen snapshot).
        var hsCodeRule = DummyRule("HSCODE-001", RuleCategory.HSCode, 5, "HSCode", ConditionOperator.Equals, "1234.56", "ControlledGoodsFlag", "true");
        var permitRule = DummyRule("PERMIT-001", RuleCategory.Permit, 5, "ControlledGoodsFlag", ConditionOperator.Equals, "true", "PermitRequired", "true");
        var engine = BuildEngine(hsCodeRule, permitRule);
        var facts = new Facts { ["HSCode"] = "1234.56" };
        var context = new EvaluationContext(Guid.NewGuid(), SubjectType.Shipment, new DateOnly(2026, 6, 1));

        var result = await engine.EvaluateAsync(context, facts);

        Assert.Equal("true", result.Facts["ControlledGoodsFlag"]);
        Assert.Equal("true", result.Facts["PermitRequired"]);
    }

    [Fact]
    public async Task EvaluateAsync_ProducesOneTraceStepPerPipelineStage_IncludingNoMatchSteps()
    {
        var engine = BuildEngine();
        var facts = new Facts();
        var context = new EvaluationContext(Guid.NewGuid(), SubjectType.Shipment, new DateOnly(2026, 6, 1));

        var result = await engine.EvaluateAsync(context, facts);

        Assert.Equal(PipelineStages.Ordered.Count, result.Trace.Count);
        Assert.All(result.Trace, step => Assert.Equal("no match", step.Outcome));
    }

    [Fact]
    public async Task EvaluateAsync_WinningRule_RecordsRuleCodeInTrace()
    {
        var deMinimisRule = DummyRule("DEMINIMIS-001", RuleCategory.DeMinimis, 5, "ShipmentValue", ConditionOperator.LessThan, "5000", "ClearanceType", "DEM");
        var engine = BuildEngine(deMinimisRule);
        var facts = new Facts { ["ShipmentValue"] = 2000m };
        var context = new EvaluationContext(Guid.NewGuid(), SubjectType.Shipment, new DateOnly(2026, 6, 1));

        var result = await engine.EvaluateAsync(context, facts);

        var deMinimisStep = result.Trace.Single(s => s.StepName == "De Minimis Rule");
        Assert.Equal("DEMINIMIS-001", deMinimisStep.RuleCode);
        Assert.Equal("ClearanceType = DEM", deMinimisStep.Outcome);
    }
}
