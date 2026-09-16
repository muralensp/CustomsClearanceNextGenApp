namespace CustomsClearance.Platform.RulesEngine;

/// <summary>
/// PRD Ch.5.7's fixed pipeline stage categories - the same enum the Day 19
/// spike (spike/RulesEngineSpike/Engine.cs) validated against 3 real
/// scenarios. Carried into the real schema unchanged: a Rule's Category
/// determines which Day 32 pipeline stage loads it (ADR-0013's
/// IRuleSetLoader.LoadAsync(RuleCategory[] categories, ...)).
/// </summary>
public enum RuleCategory
{
    Country,
    Customer,
    Shipment,
    DeMinimis,
    Commodity,
    HSCode,
    Duty,
    Permit,
    Warning,
}
