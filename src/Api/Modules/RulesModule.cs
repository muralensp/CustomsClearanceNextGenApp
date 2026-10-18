using CustomsClearance.Api.RulesAdmin;
using CustomsClearance.Platform.RulesEngine;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Api.Modules;

/// <summary>
/// Days 31-35: the Rule/RuleVersion/RuleCondition schema (ADR-0002/ADR-0013),
/// the evaluation pipeline, the Admin API, and the dev-only diagnostic
/// endpoints that proved them. Enabled by ConnectionStrings:RulesDb.
/// </summary>
public sealed class RulesModule : IApiModule
{
    private const string _connectionName = "RulesDb";

    public string Name => "RulesEngine";

    public int Order => 30;

    public void ConfigureServices(WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(_connectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        builder.Services.AddDbContext<RulesDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddScoped<RuleRepository>();
        builder.Services.AddScoped<RuleVersionRepository>();
        builder.Services.AddScoped<RuleConditionRepository>();

        // The evaluation pipeline (IRuleSetLoader/IConditionEvaluator/
        // IPriorityResolver/IDecisionTraceRecorder + RulesEngine orchestrator)
        // sits behind the same RulesDb-presence guard as the schema itself.
        builder.Services.AddCustomsClearanceRulesEngine();
    }

    public async Task InitializeAsync(WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(_connectionName)))
        {
            return;
        }

        await DatabaseBootstrap.WithRetryAsync(app, _connectionName, async () =>
        {
            using var scope = app.Services.CreateScope();
            await DatabaseBootstrap.EnsureSchemaAsync(scope.ServiceProvider.GetRequiredService<RulesDbContext>());
        });
    }

    public void MapEndpoints(WebApplication app)
    {
        // Same connection-string-presence guard as every RulesDb-dependent
        // endpoint (Minimal API's RequestDelegateFactory validates parameter
        // bindability at MAP time, so these must only be mapped when
        // RulesDbContext is registered).
        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(_connectionName)))
        {
            return;
        }

        // Day 34: the real (not diagnostic) minimal Rules admin API.
        // RequireAdmin-gated (confirmed with the user) - this changes real
        // compliance policy, the same category of consequential action as
        // /diagnostics/secure.
        app.MapRuleAdminEndpoints();

        if (app.Environment.IsDevelopment())
        {
            MapDiagnosticEndpoints(app);
        }
    }

    private static void MapDiagnosticEndpoints(WebApplication app)
    {
        // Day 31: proves RulesDbContext's real schema (Rule -> RuleVersion ->
        // RuleCondition, with foreign keys) against real PostgreSQL - same
        // dev-only diagnostic-endpoint proof pattern as Diagnostics/widgets
        // (Day 18) and IdentityDb (Day 23). Not a real admin API (that's
        // Day 34) - just enough to prove the migrated schema round-trips a
        // full Rule graph, including the RuleId/RuleVersionId foreign keys.
        app.MapPost("/diagnostics/rules", async (
            RuleRepository ruleRepository,
            RuleVersionRepository versionRepository,
            RuleConditionRepository conditionRepository,
            RulesDbContext dbContext) =>
        {
            var rule = new Rule
            {
                Id = Guid.NewGuid(),
                RuleCode = $"DIAG-{Guid.NewGuid():N}"[..16],
                Name = "Diagnostics sample rule",
                Description = "Day 31 schema proof - not a real business rule.",
                Category = RuleCategory.DeMinimis,
                Priority = 9,
            };
            await ruleRepository.AddAsync(rule);

            var version = new RuleVersion
            {
                Id = Guid.NewGuid(),
                RuleId = rule.Id,
                VersionNumber = 1,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                EffectiveTo = null,
                OutcomeFactName = "ClearanceType",
                OutcomeValue = "DEM",
            };
            await versionRepository.AddAsync(version);

            var condition = new RuleCondition
            {
                Id = Guid.NewGuid(),
                RuleVersionId = version.Id,
                FactName = "ShipmentValue",
                Operator = ConditionOperator.LessThan,
                Value = "5000",
            };
            await conditionRepository.AddAsync(condition);

            await dbContext.SaveChangesAsync();

            return Results.Ok(new { rule.Id, rule.RuleCode, VersionId = version.Id, ConditionId = condition.Id });
        }).WithName("DiagnosticsCreateRule");

        // Day 32: proves the real evaluation pipeline (EfRuleSetLoader
        // against real PostgreSQL, ConditionEvaluator, PriorityResolver,
        // RulesEngine orchestration) end-to-end - not just against EF
        // Core's InMemory provider (Platform.RulesEngine.Tests). Seeds one
        // De Minimis rule (ShipmentValue < 5000 -> ClearanceType = DEM)
        // if none exists yet, then evaluates the supplied shipmentValue
        // against it.
        app.MapPost("/diagnostics/rules/evaluate", async (
            decimal shipmentValue,
            RulesDbContext dbContext,
            RulesEngine engine) =>
        {
            const string seedRuleCode = "DIAG-EVAL-DEMINIMIS";
            if (!await dbContext.Rules.AnyAsync(r => r.RuleCode == seedRuleCode))
            {
                var rule = new Rule
                {
                    Id = Guid.NewGuid(),
                    RuleCode = seedRuleCode,
                    Name = "Diagnostics evaluation seed rule",
                    Description = "Day 32 pipeline proof - not a real business rule.",
                    Category = RuleCategory.DeMinimis,
                    Priority = 5,
                };
                var version = new RuleVersion
                {
                    Id = Guid.NewGuid(),
                    RuleId = rule.Id,
                    VersionNumber = 1,
                    EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                    OutcomeFactName = "ClearanceType",
                    OutcomeValue = "DEM",
                };
                var condition = new RuleCondition
                {
                    Id = Guid.NewGuid(),
                    RuleVersionId = version.Id,
                    FactName = "ShipmentValue",
                    Operator = ConditionOperator.LessThan,
                    Value = "5000",
                };
                await dbContext.Rules.AddAsync(rule);
                await dbContext.RuleVersions.AddAsync(version);
                await dbContext.RuleConditions.AddAsync(condition);
                await dbContext.SaveChangesAsync();
            }

            var facts = new Facts { ["ShipmentValue"] = shipmentValue };
            var context = new EvaluationContext(Guid.NewGuid(), SubjectType.Shipment, DateOnly.FromDateTime(DateTime.UtcNow));

            var result = await engine.EvaluateAsync(context, facts);

            return Results.Ok(new
            {
                evaluationId = result.EvaluationId,
                clearanceType = result.Facts["ClearanceType"],
                trace = result.Trace.Select(step => new { step.StepOrder, step.StepName, step.RuleCode, step.Outcome }),
            });
        }).WithName("DiagnosticsEvaluateRules");

        // Day 35 (Rules Engine Review): the Day 32 endpoint above only
        // ever supplies one fact (ShipmentValue), which can't exercise
        // cross-stage chaining (e.g. an HS Code rule setting
        // ControlledGoodsFlag for a later Permit rule to read) - the
        // exact behavior ADR-0013 built Facts as mutable-in-place to
        // support. This accepts an arbitrary fact bag so real
        // India-specific sample rules loaded through the actual Day 34
        // admin API (not hand-seeded here) can be evaluated end to end.
        app.MapPost("/diagnostics/rules/evaluate-facts", async (
            Dictionary<string, string> facts,
            RulesEngine engine) =>
        {
            var factBag = new Facts();
            foreach (var (factName, value) in facts)
            {
                factBag[factName] = value;
            }

            var context = new EvaluationContext(Guid.NewGuid(), SubjectType.Shipment, DateOnly.FromDateTime(DateTime.UtcNow));
            var result = await engine.EvaluateAsync(context, factBag);

            return Results.Ok(new
            {
                evaluationId = result.EvaluationId,
                facts = result.Facts.Snapshot(),
                trace = result.Trace.Select(step => new { step.StepOrder, step.StepName, step.RuleCode, step.Outcome }),
            });
        }).WithName("DiagnosticsEvaluateRulesWithFacts");

        // Day 33: the actual deliverable ("decision trace visible for a
        // test rule evaluation") - proves EfDecisionTraceRecorder's rows
        // are genuinely queryable back out of PostgreSQL by EvaluationId,
        // independent of the in-process result the evaluate call above
        // already returned. A real caller (e.g. a later compliance-review
        // screen) would use exactly this query shape.
        app.MapGet("/diagnostics/rules/trace/{evaluationId:guid}", async (Guid evaluationId, RulesDbContext dbContext) =>
        {
            var steps = await dbContext.DecisionTraces
                .Where(t => t.EvaluationId == evaluationId)
                .OrderBy(t => t.StepOrder)
                .Select(t => new
                {
                    t.StepOrder,
                    t.StepName,
                    t.RuleId,
                    t.RuleVersionId,
                    t.Outcome,
                    t.SubjectId,
                    SubjectType = t.SubjectType.ToString(),
                    t.EvaluationDate,
                    t.FactsSnapshotJson,
                })
                .ToListAsync();

            return steps.Count == 0 ? Results.NotFound() : Results.Ok(steps);
        }).WithName("DiagnosticsGetRuleTrace");

        app.MapGet("/diagnostics/rules/{id:guid}", async (Guid id, RulesDbContext dbContext) =>
        {
            // Projected to an anonymous DTO, not the raw Rule entity: Rule
            // <-> RuleVersion <-> RuleCondition's navigation properties are
            // bidirectional (Rule.Versions / RuleVersion.Rule), which
            // System.Text.Json's default serializer cannot walk - found for
            // real (Day 31 live verification): a bare Results.Ok(rule)
            // threw "A possible object cycle was detected" at the first
            // GET. Real bounded-context endpoints (Day 34+ admin API) will
            // need this same projection discipline, not raw entity returns.
            var rule = await dbContext.Rules
                .Where(r => r.Id == id)
                .Select(r => new
                {
                    r.Id,
                    r.RuleCode,
                    r.Name,
                    r.Description,
                    Category = r.Category.ToString(),
                    r.Priority,
                    r.Active,
                    Versions = r.Versions.Select(v => new
                    {
                        v.Id,
                        v.VersionNumber,
                        v.EffectiveFrom,
                        v.EffectiveTo,
                        v.OutcomeFactName,
                        v.OutcomeValue,
                        Conditions = v.Conditions.Select(c => new
                        {
                            c.Id,
                            c.FactName,
                            Operator = c.Operator.ToString(),
                            c.Value,
                        }),
                    }),
                })
                .SingleOrDefaultAsync();

            return rule is null ? Results.NotFound() : Results.Ok(rule);
        }).WithName("DiagnosticsGetRule");
    }
}
