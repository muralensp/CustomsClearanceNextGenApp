using CustomsClearance.Platform.Audit;
using CustomsClearance.Platform.ErrorHandling;
using CustomsClearance.Platform.RulesEngine;
using CustomsClearance.Platform.Security;
using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Api.RulesAdmin;

/// <summary>
/// Day 34's real (not diagnostic) minimal admin API: create/update/
/// deactivate a Rule, plus add a new RuleVersion - the actual way a
/// compliance admin's policy change (ADR-0002's whole reason for a
/// configuration-driven engine) reaches the system, no code release
/// required. Gated by PlatformPolicies.RequireAdmin (confirmed with the
/// user): this changes what clearance decisions the engine makes, the
/// same category of consequential action as /diagnostics/secure already
/// requires Admin for.
///
/// Enforces two invariants Day 31/33 (ADR-0025/ADR-0027) explicitly
/// deferred to "the Day 34 admin API": RuleCode uniqueness (checked here,
/// not just relying on the unique index, so a violation returns a real
/// DomainValidationException/422 instead of a raw 500 from a DB
/// constraint failure) and no two RuleVersions of the same Rule may have
/// overlapping effective-date windows.
/// </summary>
public static class RuleAdminEndpoints
{
    public static WebApplication MapRuleAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/rules").RequireAuthorization(PlatformPolicies.RequireAdmin);

        group.MapPost("/", CreateRuleAsync).WithName("CreateRule");
        group.MapGet("/", ListRulesAsync).WithName("ListRules");
        group.MapGet("/{id:guid}", GetRuleAsync).WithName("GetRule");
        group.MapPut("/{id:guid}", UpdateRuleAsync).WithName("UpdateRule");
        group.MapPost("/{id:guid}/deactivate", DeactivateRuleAsync).WithName("DeactivateRule");
        group.MapPost("/{id:guid}/versions", AddRuleVersionAsync).WithName("AddRuleVersion");

        return app;
    }

    private static async Task<IResult> CreateRuleAsync(CreateRuleRequest request, HttpContext http, RulesDbContext dbContext, IAuditWriter auditWriter, CancellationToken cancellationToken)
    {
        ValidateEffectiveWindow(request.EffectiveFrom, request.EffectiveTo);
        ValidateConditions(request.Conditions);

        if (await dbContext.Rules.AnyAsync(r => r.RuleCode == request.RuleCode, cancellationToken))
        {
            throw new DomainValidationException("BR-RULE-001", $"RuleCode '{request.RuleCode}' is already in use.", nameof(request.RuleCode));
        }

        var rule = new Rule
        {
            Id = Guid.NewGuid(),
            RuleCode = request.RuleCode,
            Name = request.Name,
            Description = request.Description,
            Category = request.Category,
            Priority = request.Priority,
            Active = true,
        };
        var version = new RuleVersion
        {
            Id = Guid.NewGuid(),
            RuleId = rule.Id,
            VersionNumber = 1,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            OutcomeFactName = request.OutcomeFactName,
            OutcomeValue = request.OutcomeValue,
        };
        var conditions = request.Conditions.Select(c => new RuleCondition
        {
            Id = Guid.NewGuid(),
            RuleVersionId = version.Id,
            FactName = c.FactName,
            Operator = c.Operator,
            Value = c.Value,
        }).ToList();

        dbContext.Rules.Add(rule);
        dbContext.RuleVersions.Add(version);
        dbContext.RuleConditions.AddRange(conditions);
        await dbContext.SaveChangesAsync(cancellationToken);

        await RuleAudit.WriteAsync(auditWriter, http, "RuleCreated", rule.Id,
        [
            new FieldChange(nameof(Rule.RuleCode), null, rule.RuleCode),
            new FieldChange(nameof(Rule.Name), null, rule.Name),
            new FieldChange(nameof(Rule.Category), null, rule.Category.ToString()),
            new FieldChange(nameof(Rule.Priority), null, rule.Priority.ToString()),
            .. RuleAudit.VersionChanges(version, conditions),
        ], cancellationToken);

        return Results.Created($"/api/rules/{rule.Id}", await LoadDetailAsync(dbContext, rule.Id, cancellationToken));
    }

    private static async Task<IResult> ListRulesAsync(RulesDbContext dbContext, CancellationToken cancellationToken)
    {
        var rules = await dbContext.Rules
            .Select(r => new RuleSummary(r.Id, r.RuleCode, r.Name, r.Category.ToString(), r.Priority, r.Active, r.Versions.Count))
            .ToListAsync(cancellationToken);

        return Results.Ok(rules);
    }

    private static async Task<IResult> GetRuleAsync(Guid id, RulesDbContext dbContext, CancellationToken cancellationToken)
    {
        var detail = await LoadDetailAsync(dbContext, id, cancellationToken);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> UpdateRuleAsync(Guid id, UpdateRuleRequest request, HttpContext http, RulesDbContext dbContext, IAuditWriter auditWriter, CancellationToken cancellationToken)
    {
        var rule = await dbContext.Rules.FindAsync([id], cancellationToken);
        if (rule is null)
        {
            return Results.NotFound();
        }

        // Only the Rule's own mutable fields - RuleCode/Category are
        // identity (never editable); conditions/outcome are RuleVersion's
        // concern, changed only via AddRuleVersionAsync.
        var (oldName, oldDescription, oldPriority) = (rule.Name, rule.Description, rule.Priority);
        rule.Name = request.Name;
        rule.Description = request.Description;
        rule.Priority = request.Priority;
        await dbContext.SaveChangesAsync(cancellationToken);

        // Only genuine changes are audited, as for config updates.
        var changes = new List<FieldChange>();
        if (oldName != rule.Name)
        {
            changes.Add(new FieldChange(nameof(Rule.Name), oldName, rule.Name));
        }

        if (oldDescription != rule.Description)
        {
            changes.Add(new FieldChange(nameof(Rule.Description), oldDescription, rule.Description));
        }

        if (oldPriority != rule.Priority)
        {
            changes.Add(new FieldChange(nameof(Rule.Priority), oldPriority.ToString(), rule.Priority.ToString()));
        }

        if (changes.Count > 0)
        {
            await RuleAudit.WriteAsync(auditWriter, http, "RuleUpdated", rule.Id, changes, cancellationToken);
        }

        return Results.Ok(await LoadDetailAsync(dbContext, id, cancellationToken));
    }

    private static async Task<IResult> DeactivateRuleAsync(Guid id, HttpContext http, RulesDbContext dbContext, IAuditWriter auditWriter, CancellationToken cancellationToken)
    {
        var rule = await dbContext.Rules.FindAsync([id], cancellationToken);
        if (rule is null)
        {
            return Results.NotFound();
        }

        // Soft-deactivation only, per ADR-0025 - a Rule is never hard-
        // deleted, since past DecisionTrace rows still reference
        // RuleVersions authored under it.
        var wasActive = rule.Active;
        rule.Active = false;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (wasActive)
        {
            await RuleAudit.WriteAsync(auditWriter, http, "RuleDeactivated", rule.Id,
                [new FieldChange(nameof(Rule.Active), bool.TrueString, bool.FalseString)], cancellationToken);
        }

        return Results.Ok(await LoadDetailAsync(dbContext, id, cancellationToken));
    }

    private static async Task<IResult> AddRuleVersionAsync(Guid id, AddRuleVersionRequest request, HttpContext http, RulesDbContext dbContext, IAuditWriter auditWriter, CancellationToken cancellationToken)
    {
        var rule = await dbContext.Rules
            .Include(r => r.Versions)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rule is null)
        {
            return Results.NotFound();
        }

        ValidateEffectiveWindow(request.EffectiveFrom, request.EffectiveTo);
        ValidateConditions(request.Conditions);

        // Effective-dating invariant (ADR-0013/ADR-0025's deferred-to-
        // Day-34 check) plus the supersede exception found via live
        // testing and confirmed with the user - both are pure logic,
        // extracted to RuleVersionEffectiveDateValidator (Platform
        // .RulesEngine) so they're covered by that project's 95%
        // core-tier gate, not proven only by this endpoint's live testing.
        var validation = RuleVersionEffectiveDateValidator.Validate(rule.Versions.ToList(), request.EffectiveFrom, request.EffectiveTo);
        if (validation.HasOverlap)
        {
            throw new DomainValidationException(
                "BR-RULE-002",
                $"The requested effective window ({request.EffectiveFrom:yyyy-MM-dd} - {request.EffectiveTo?.ToString("yyyy-MM-dd") ?? "open-ended"}) overlaps an existing RuleVersion for this Rule.",
                nameof(request.EffectiveFrom));
        }

        var changes = new List<FieldChange>();
        if (validation.VersionToSupersede is { } superseded)
        {
            changes.Add(new FieldChange($"Version{superseded.VersionNumber}.EffectiveTo", superseded.EffectiveTo?.ToString("yyyy-MM-dd"), request.EffectiveFrom.AddDays(-1).ToString("yyyy-MM-dd")));

            // This does not rewrite history. Past evaluations used a date
            // inside the old version's now-closed window, so they remain
            // unaffected. DecisionTrace rows already recorded still
            // reference the same RuleVersionId with its original outcome
            // fact, outcome value, and conditions - only EffectiveTo
            // changes for future evaluations.
            superseded.EffectiveTo = request.EffectiveFrom.AddDays(-1);
        }

        var nextVersionNumber = rule.Versions.Count == 0 ? 1 : rule.Versions.Max(v => v.VersionNumber) + 1;
        var version = new RuleVersion
        {
            Id = Guid.NewGuid(),
            RuleId = rule.Id,
            VersionNumber = nextVersionNumber,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            OutcomeFactName = request.OutcomeFactName,
            OutcomeValue = request.OutcomeValue,
        };
        var conditions = request.Conditions.Select(c => new RuleCondition
        {
            Id = Guid.NewGuid(),
            RuleVersionId = version.Id,
            FactName = c.FactName,
            Operator = c.Operator,
            Value = c.Value,
        }).ToList();

        dbContext.RuleVersions.Add(version);
        dbContext.RuleConditions.AddRange(conditions);
        await dbContext.SaveChangesAsync(cancellationToken);

        changes.AddRange(RuleAudit.VersionChanges(version, conditions));
        await RuleAudit.WriteAsync(auditWriter, http, "RuleVersionAdded", rule.Id, changes, cancellationToken);

        return Results.Created($"/api/rules/{rule.Id}", await LoadDetailAsync(dbContext, rule.Id, cancellationToken));
    }

    private static void ValidateEffectiveWindow(DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (effectiveTo is { } to && to < effectiveFrom)
        {
            throw new DomainValidationException("BR-RULE-003", "EffectiveTo must not be earlier than EffectiveFrom.", "EffectiveTo");
        }
    }

    private static void ValidateConditions(IReadOnlyList<ConditionRequest> conditions)
    {
        if (conditions.Count == 0)
        {
            throw new DomainValidationException("BR-RULE-004", "A RuleVersion must have at least one condition.", "Conditions");
        }
    }

    // Projected to named DTOs, not raw entities - Rule <-> RuleVersion <->
    // RuleCondition's bidirectional navigation properties throw a JSON
    // serialization cycle if returned directly (the same real bug found
    // and fixed on Day 31's diagnostic endpoint).
    private static async Task<RuleDetail?> LoadDetailAsync(RulesDbContext dbContext, Guid id, CancellationToken cancellationToken) =>
        await dbContext.Rules
            .Where(r => r.Id == id)
            .Select(r => new RuleDetail(
                r.Id,
                r.RuleCode,
                r.Name,
                r.Description,
                r.Category.ToString(),
                r.Priority,
                r.Active,
                r.Versions.OrderBy(v => v.VersionNumber).Select(v => new RuleVersionDetail(
                    v.Id,
                    v.VersionNumber,
                    v.EffectiveFrom,
                    v.EffectiveTo,
                    v.OutcomeFactName,
                    v.OutcomeValue,
                    v.Conditions.Select(c => new ConditionDetail(c.Id, c.FactName, c.Operator.ToString(), c.Value)).ToList()))
                .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
}
