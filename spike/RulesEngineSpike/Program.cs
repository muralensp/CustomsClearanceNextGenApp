using RulesEngineSpike;

// PRD Ch.5.10 decision-table rules, encoded 1:1. Priorities per PRD Ch.5.9
// (1=Legal Restrictions ... 9=Default Rules). This is the "throwaway spike prototype validating
// this design against 2-3 PRD Ch.5.10 decision-table scenarios" called for by ADR-0013's
// Consequences section — not the Phase 0 build (no persistence, no admin authoring).
List<Rule> rules =
[
    new("R-COUNTRY-DOM", RuleCategory.Country, 2, "DOM",
        [new("OriginEqualsDestinationSG", ConditionOperator.Equals, true)]),

    new("R-FTZ", RuleCategory.Shipment, 5, "FTZ",
        [new("DestinationInFtz", ConditionOperator.Equals, true)]),

    new("R-HSCODE-CONTROLLED", RuleCategory.HSCode, 3, "FML",
        [new("HsCodeControlled", ConditionOperator.Equals, true)]),

    new("R-PERMIT-REQUIRED", RuleCategory.Permit, 4, "FML",
        [new("PermitRequired", ConditionOperator.Equals, true)]),

    new("R-DUTY-POSITIVE", RuleCategory.Duty, 7, "FML",
        [new("Duty", ConditionOperator.GreaterThan, 0m)]),

    new("R-DE-MINIMIS", RuleCategory.DeMinimis, 7, "DEM",
        [new("ImportValueBelowDeMinimisThreshold", ConditionOperator.Equals, true)]),

    new("R-ALTERNATE-BROKER", RuleCategory.Customer, 6, "ABK",
        [new("AlternateBrokerCustomer", ConditionOperator.Equals, true)]),

    new("R-DEFAULT-INF", RuleCategory.Warning, 9, "INF",
        [new("ClearanceTypeAssigned", ConditionOperator.Equals, false)]),
];

var loader = new InMemoryRuleSetLoader(rules);
var evaluator = new ConditionEvaluator();
var resolver = new PriorityResolver();

int passed = 0, failed = 0;

RunScenario("FTZ", "PRD 5.10: Destination in FTZ -> CT = FTZ", "FTZ", () =>
{
    var facts = BaseFacts();
    facts["DestinationInFtz"] = true;
    return facts;
});

RunScenario("HS Code Controlled", "PRD 5.10: HS Code Controlled -> CT = FML", "FML", () =>
{
    var facts = BaseFacts();
    facts["HsCodeControlled"] = true;
    return facts;
});

RunScenario("De Minimis", "PRD 5.10: Import Value below De Minimis threshold -> CT = DEM", "DEM", () =>
{
    var facts = BaseFacts();
    facts["ImportValueBelowDeMinimisThreshold"] = true;
    return facts;
});

Console.WriteLine();
Console.WriteLine($"Result: {passed} passed, {failed} failed.");
Environment.Exit(failed == 0 ? 0 : 1);

Facts BaseFacts()
{
    var f = new Facts();
    f["OriginEqualsDestinationSG"] = false;
    f["DestinationInFtz"] = false;
    f["HsCodeControlled"] = false;
    f["PermitRequired"] = false;
    f["Duty"] = 0m;
    f["ImportValueBelowDeMinimisThreshold"] = false;
    f["AlternateBrokerCustomer"] = false;
    f["ClearanceTypeAssigned"] = false;
    return f;
}

void RunScenario(string name, string description, string expectedClearanceType, Func<Facts> buildFacts)
{
    var tracer = new InMemoryDecisionTraceRecorder();
    var engine = new RulesEngine(loader, evaluator, resolver, tracer);
    var result = engine.Evaluate(buildFacts());

    var ok = result.ClearanceType == expectedClearanceType;
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] Scenario: {name} — {description}");
    Console.WriteLine($"       Expected CT={expectedClearanceType}, Actual CT={result.ClearanceType ?? "(none)"}");
    foreach (var step in result.Traces)
    {
        Console.WriteLine($"       Step {step.StepOrder} {step.StepName,-15} RuleId={step.RuleId ?? "(none)",-22} Outcome={step.Outcome}");
    }
    Console.WriteLine();

    if (ok) passed++; else failed++;
}
