namespace CustomsClearance.Platform.RulesEngine;

/// <summary>One PRD Ch.5.7 pipeline stage: which RuleCategory values IRuleSetLoader loads for it, in the fixed order the pipeline evaluates.</summary>
public sealed record PipelineStage(int StepOrder, string Name, RuleCategory[] Categories);

/// <summary>
/// PRD Ch.5.7's fixed stage order, hardcoded per ADR-0013: the *rules* are
/// config-driven (ADR-0002), but *pipeline stage order* is a product/
/// architecture decision requiring a release to change, not rule-authoring
/// policy. Matches the full Day 3/PRD sequence (Country -> Customer ->
/// Shipment -> Commodity -> HS Code -> Duty -> Permit -> Shipment
/// Indicators/Warning -> Final CT Assignment), mapped onto Day 31's
/// RuleCategory enum (DeMinimis folded into the Shipment stage - the Day
/// 19 spike's finding that RuleCategory values must be distinct per stage,
/// not shared, still applies: DeMinimis has its own category and its own
/// stage here, not reused under Shipment).
/// </summary>
public static class PipelineStages
{
    public static readonly IReadOnlyList<PipelineStage> Ordered =
    [
        new(1, "Country Rule", [RuleCategory.Country]),
        new(2, "Customer Rule", [RuleCategory.Customer]),
        new(3, "Shipment Rule", [RuleCategory.Shipment]),
        new(4, "De Minimis Rule", [RuleCategory.DeMinimis]),
        new(5, "Commodity Rule", [RuleCategory.Commodity]),
        new(6, "HS Code Rule", [RuleCategory.HSCode]),
        new(7, "Duty Rule", [RuleCategory.Duty]),
        new(8, "Permit Rule", [RuleCategory.Permit]),
        new(9, "Warning Rule", [RuleCategory.Warning]),
    ];
}

/// <summary>A Rule loaded together with its one currently-effective RuleVersion and that version's conditions (ADR-0013's IRuleSetLoader output shape).</summary>
public sealed record ApplicableRule(Rule Rule, RuleVersion Version, IReadOnlyList<RuleCondition> Conditions);
