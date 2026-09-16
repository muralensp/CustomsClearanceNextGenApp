using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CustomsClearance.Platform.RulesEngine.Tests;

/// <summary>EF Core InMemory provider (ADR-0009). Real PostgreSQL persistence proven separately via docker-compose (Day 33 Execution Log), same split as every other RulesDbContext-backed class.</summary>
public class EfDecisionTraceRecorderTests
{
    private static RulesDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RulesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ApplicableRule DummyWinningRule() => new(
        new Rule { Id = Guid.NewGuid(), RuleCode = "TRACE-001", Name = "TRACE-001", Category = RuleCategory.DeMinimis, Priority = 5 },
        new RuleVersion { Id = Guid.NewGuid(), RuleId = Guid.NewGuid(), VersionNumber = 1, EffectiveFrom = new DateOnly(2026, 1, 1), OutcomeFactName = "ClearanceType", OutcomeValue = "DEM" },
        []);

    [Fact]
    public async Task RecordStep_ThenFlushAsync_PersistsOneRowPerStep()
    {
        await using var context = CreateContext();
        var recorder = new EfDecisionTraceRecorder(context);
        var evaluationId = Guid.NewGuid();
        var evalContext = new EvaluationContext(Guid.NewGuid(), SubjectType.Shipment, new DateOnly(2026, 6, 1));
        var facts = new Facts { ["ShipmentValue"] = 2000m };

        recorder.RecordStep(evaluationId, evalContext, null, facts, 1, "Country Rule");
        recorder.RecordStep(evaluationId, evalContext, DummyWinningRule(), facts, 2, "Customer Rule");
        var steps = await recorder.FlushAsync();

        Assert.Equal(2, steps.Count);
        Assert.Equal(2, await context.DecisionTraces.CountAsync());
    }

    [Fact]
    public async Task RecordStep_NoMatch_PersistsNullRuleIdAndVersionId()
    {
        await using var context = CreateContext();
        var recorder = new EfDecisionTraceRecorder(context);
        var evaluationId = Guid.NewGuid();
        var evalContext = new EvaluationContext(Guid.NewGuid(), SubjectType.Shipment, new DateOnly(2026, 6, 1));

        recorder.RecordStep(evaluationId, evalContext, null, new Facts(), 1, "Country Rule");
        await recorder.FlushAsync();

        var saved = await context.DecisionTraces.SingleAsync();
        Assert.Null(saved.RuleId);
        Assert.Null(saved.RuleVersionId);
        Assert.Equal("no match", saved.Outcome);
    }

    [Fact]
    public async Task RecordStep_WinningRule_PersistsRuleAndVersionIds()
    {
        await using var context = CreateContext();
        var recorder = new EfDecisionTraceRecorder(context);
        var evaluationId = Guid.NewGuid();
        var evalContext = new EvaluationContext(Guid.NewGuid(), SubjectType.Shipment, new DateOnly(2026, 6, 1));
        var winner = DummyWinningRule();

        recorder.RecordStep(evaluationId, evalContext, winner, new Facts(), 4, "De Minimis Rule");
        await recorder.FlushAsync();

        var saved = await context.DecisionTraces.SingleAsync();
        Assert.Equal(winner.Rule.Id, saved.RuleId);
        Assert.Equal(winner.Version.Id, saved.RuleVersionId);
        Assert.Equal("ClearanceType = DEM", saved.Outcome);
    }

    [Fact]
    public async Task RecordStep_PersistsFactsSnapshotAsJson()
    {
        await using var context = CreateContext();
        var recorder = new EfDecisionTraceRecorder(context);
        var evaluationId = Guid.NewGuid();
        var evalContext = new EvaluationContext(Guid.NewGuid(), SubjectType.Shipment, new DateOnly(2026, 6, 1));
        var facts = new Facts { ["ShipmentValue"] = 2000m };

        recorder.RecordStep(evaluationId, evalContext, null, facts, 1, "Country Rule");
        await recorder.FlushAsync();

        var saved = await context.DecisionTraces.SingleAsync();
        Assert.Contains("ShipmentValue", saved.FactsSnapshotJson);
    }

    [Fact]
    public async Task FlushAsync_TwoDifferentEvaluations_CanBeQueriedSeparatelyByEvaluationId()
    {
        await using var context = CreateContext();
        var evalContext = new EvaluationContext(Guid.NewGuid(), SubjectType.Shipment, new DateOnly(2026, 6, 1));

        var recorder1 = new EfDecisionTraceRecorder(context);
        var evaluationId1 = Guid.NewGuid();
        recorder1.RecordStep(evaluationId1, evalContext, null, new Facts(), 1, "Country Rule");
        await recorder1.FlushAsync();

        var recorder2 = new EfDecisionTraceRecorder(context);
        var evaluationId2 = Guid.NewGuid();
        recorder2.RecordStep(evaluationId2, evalContext, null, new Facts(), 1, "Country Rule");
        recorder2.RecordStep(evaluationId2, evalContext, null, new Facts(), 2, "Customer Rule");
        await recorder2.FlushAsync();

        var evaluation1Steps = await context.DecisionTraces.Where(t => t.EvaluationId == evaluationId1).ToListAsync();
        var evaluation2Steps = await context.DecisionTraces.Where(t => t.EvaluationId == evaluationId2).ToListAsync();

        Assert.Single(evaluation1Steps);
        Assert.Equal(2, evaluation2Steps.Count);
    }
}
