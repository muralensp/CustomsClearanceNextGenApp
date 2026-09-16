using CustomsClearance.Platform.RulesEngine;

namespace CustomsClearance.Api.RulesAdmin;

public sealed record ConditionRequest(string FactName, ConditionOperator Operator, string Value);

/// <summary>Creates a Rule together with its first RuleVersion + Conditions in one call - a brand-new Rule has no prior version to conflict with, so there's no effective-dating check to run yet (unlike AddRuleVersionRequest below).</summary>
public sealed record CreateRuleRequest(
    string RuleCode,
    string Name,
    string? Description,
    RuleCategory Category,
    int Priority,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string OutcomeFactName,
    string OutcomeValue,
    IReadOnlyList<ConditionRequest> Conditions);

/// <summary>Updates only the Rule's own mutable fields - RuleCode/Category are identity, never editable after creation; conditions/outcome live on RuleVersion, changed only via AddRuleVersionRequest (per the "RuleVersion is immutable-once-published" design).</summary>
public sealed record UpdateRuleRequest(string Name, string? Description, int Priority);

public sealed record AddRuleVersionRequest(
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string OutcomeFactName,
    string OutcomeValue,
    IReadOnlyList<ConditionRequest> Conditions);

public sealed record RuleSummary(Guid Id, string RuleCode, string Name, string Category, int Priority, bool Active, int VersionCount);

public sealed record RuleVersionDetail(Guid Id, int VersionNumber, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string OutcomeFactName, string OutcomeValue, IReadOnlyList<ConditionDetail> Conditions);

public sealed record ConditionDetail(Guid Id, string FactName, string Operator, string Value);

public sealed record RuleDetail(Guid Id, string RuleCode, string Name, string? Description, string Category, int Priority, bool Active, IReadOnlyList<RuleVersionDetail> Versions);
