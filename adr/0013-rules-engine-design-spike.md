# ADR-0013: Rules-Engine Evaluation Pipeline Design (Day 19 Spike)

**Status:** Proposed
**Date:** 2026-08-12
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

ADR-0002 committed to a custom, in-house, configuration-driven rules engine built around the
Rule / RuleVersion / RuleCondition / DecisionTrace schema (Day 3 ER Diagram v0.2), with full
build-out scoped to Phase 0 Days 31-35. Day 19 is the design spike called for by ADR-0002 and the
Day-Wise Plan: work out the evaluation pipeline's shape, interfaces, and algorithm *before* Phase 0
implementation, so Days 31-35 build against a settled design instead of discovering pipeline
questions mid-build.

This ADR designs, but deliberately does not implement, the engine. Prototype validation (a
throwaway spike project proving the algorithm against real PRD scenarios) is a separate,
explicitly-gated follow-on step per the user's Day 19 instruction — not part of this document.

Inputs to this design:
- **PRD Ch.5.4** — Inputs (Shipment, Customer, Commodity, External Reference Data).
- **PRD Ch.5.7** — Rule Evaluation Pipeline (fixed category order: Country → Customer → Shipment →
  Commodity → HS Code → Duty → Permit → Shipment Indicators → Warning → Final CT Assignment →
  Status Update).
- **PRD Ch.5.9** — Rule Priority (1=Legal Restrictions … 9=Default Rules; higher priority overrides
  lower).
- **PRD Ch.5.10** — Decision Table (illustrative outcomes: DOM, FTZ, FML, DEM, ABK, INF).
- **PRD Ch.5.11-5.13** — Re-Segregation (attribute-change triggers), Manual Override (Supervisor,
  reason mandatory, audited), Audit & Explainability (DecisionTrace per firing rule).
- **Day 3 ER Diagram v0.2 §5** — the Rule/RuleVersion/RuleCondition/DecisionTrace schema this
  engine evaluates against (AND-only conditions per version; OR handled via multiple same-priority
  rules, per Day 3/Day 4 resolution).
- **ADR-0012** — Persistence (`IRepository<TEntity, TId>`, `EfRepository`) and Messaging
  (`DomainEventEnvelope<TPayload>`, `IEventPublisher`) platform libraries this engine will sit on
  top of during Phase 0.

## Decision

Design the evaluation pipeline as five composable stages, each an interface implemented as a
plain C# class with no framework dependency beyond `CustomsClearance.Platform.Persistence` (for
loading Rules/RuleVersions) — deliberately kept out of `CustomsClearance.Platform.RulesEngine`'s
own abstractions so the spike/eventual build stays testable without a database:

```
Facts assembly → Rule loading (effective-dated) → Condition matching → Priority resolution → Trace capture
```

### 1. Facts assembly — `IFactsProvider`

```csharp
public interface IFactsProvider
{
    Task<Facts> AssembleAsync(EvaluationContext context, CancellationToken ct);
}

public sealed record EvaluationContext(
    Guid SubjectId,                 // ShipmentId | CourierShipmentId | DeclarationId
    SubjectType SubjectType,        // enum: Shipment, CourierShipment, Declaration
    DateOnly EvaluationDate);       // drives RuleVersion effective-dating; defaults to today

public sealed class Facts
{
    // FactName (PRD Ch.5.4 / Day3 §5.3 enum) -> value, boxed as object but constrained
    // to the same primitive set RuleCondition.Value already assumes (string/number/bool/date).
    private readonly Dictionary<string, object?> _values;
    public object? this[string factName] => _values.GetValueOrDefault(factName);
}
```

One `IFactsProvider` implementation per `SubjectType` for MVP (`ShipmentFactsProvider`,
`CourierShipmentFactsProvider`) — each reads its own aggregate plus the reference-data lookups
(FTZ postal codes, tariff/duty tables) named in PRD Ch.5.4. `Facts` is intentionally a flat
string-keyed bag, not a strongly-typed class per subject: `RuleCondition.FactName` is already
schema-defined as an open enum (Day 3 §5.3), and a flat bag lets `RuleCondition` evaluation stay
generic (`facts[condition.FactName]`) without a switch statement per fact per subject type.

### 2. Rule loading — `IRuleSetLoader`

```csharp
public interface IRuleSetLoader
{
    Task<IReadOnlyList<ApplicableRule>> LoadAsync(
        RuleCategory[] categories, DateOnly evaluationDate, CancellationToken ct);
}

public sealed record ApplicableRule(Rule Rule, RuleVersion Version, IReadOnlyList<RuleCondition> Conditions);
```

Query shape: `Rule.Active == true AND Rule.Category IN categories AND RuleVersion.EffectiveFrom <=
evaluationDate AND (RuleVersion.EffectiveTo IS NULL OR RuleVersion.EffectiveTo >= evaluationDate)`.
Exactly one `RuleVersion` must satisfy this per `Rule` — enforced as a data invariant (no two
versions of the same rule may have overlapping, unbounded-or-equal effective windows), not
re-checked at evaluation time; a Phase-0 admin-API validation concern, out of scope here.

The `categories` parameter lets the pipeline call this once per PRD Ch.5.7 stage (Country,
Customer, Shipment, Commodity, HSCode, Duty, Permit, Warning) rather than loading the whole rule
table per evaluation — keeps each stage's evaluation set small and traceable.

### 3. Condition matching — `IConditionEvaluator`

```csharp
public interface IConditionEvaluator
{
    bool IsSatisfied(RuleCondition condition, Facts facts);
}
```

Stateless. Switches on `condition.Operator` (Equals, NotEquals, GreaterThan, LessThan, In,
Between per Day 3 §5.3) against `facts[condition.FactName]`, with `RuleCondition.Value` parsed
per-operator (scalar for Equals/NotEquals/GreaterThan/LessThan, delimited list for In, `{min,max}`
JSON for Between). A rule fires only when **all** its conditions for the active `RuleVersion`
evaluate true (AND-only, per ADR-0002/Day 3 resolution — OR is authored as separate same-priority
rules, not evaluated here).

### 4. Priority resolution — `IPriorityResolver`

```csharp
public interface IPriorityResolver
{
    ApplicableRule? Resolve(IReadOnlyList<ApplicableRule> firedRules);
}
```

Within a single PRD Ch.5.7 pipeline stage, multiple rules may fire simultaneously (e.g. two
Customer Rules both matching). `IPriorityResolver` picks the single winning outcome per stage:
lowest `Rule.Priority` number wins (PRD Ch.5.9: 1=Legal Restrictions is highest); ties are a data
authoring error surfaced as a warning in the DecisionTrace rather than a crash (first-loaded wins,
deterministically ordered by `RuleId` to keep runs repeatable). This directly matches Day 3/Day 4's
resolution that OR-logic is expressed as multiple same-priority rules — priority ties are expected
to be rare, not structurally designed for.

### 5. Trace capture — `IDecisionTraceRecorder`

```csharp
public interface IDecisionTraceRecorder
{
    void RecordStep(EvaluationContext context, ApplicableRule? winningRule, Facts factsSnapshot, int stepOrder);
    Task<IReadOnlyList<DecisionTrace>> FlushAsync(CancellationToken ct);
}
```

One `DecisionTrace` row per PRD Ch.5.7 pipeline stage (`StepOrder` = stage index), even when no
rule fires in that stage (`RuleVersionId = null`, `Outcome = "no match"`) — PRD Ch.5.13's example
trace shows a "Not applicable" step (FTZ Check), so absence-of-match is itself traceable, not
omitted. `FactsSnapshot` stores the `Facts` bag as-evaluated (immutable JSON), satisfying Day 3
§5.4's requirement that the trace survive later fact changes (e.g. CustomerWarnings edited after
the fact).

### Orchestration — `RulesEngine.EvaluateAsync`

```csharp
public sealed class RulesEngine
{
    public async Task<SegregationResult> EvaluateAsync(EvaluationContext context, CancellationToken ct)
    {
        var facts = await _factsProvider.AssembleAsync(context, ct);
        var stageOrder = PipelineStages.Ordered; // PRD Ch.5.7 fixed order, hardcoded (not configurable — the *rules* are config-driven, the *stage order* is a product decision, not policy)
        foreach (var stage in stageOrder)
        {
            var candidates = await _loader.LoadAsync(stage.Categories, context.EvaluationDate, ct);
            var fired = candidates.Where(c => c.Conditions.All(cond => _evaluator.IsSatisfied(cond, facts))).ToList();
            var winner = _resolver.Resolve(fired);
            _tracer.RecordStep(context, winner, facts, stage.StepOrder);
            if (winner is not null) ApplyOutcome(winner, facts); // e.g. sets CT, adds ShipmentIndicator — mutates `facts` so later stages see it
        }
        var traces = await _tracer.FlushAsync(ct);
        return new SegregationResult(facts, traces);
    }
}
```

`ApplyOutcome` matters: PRD Ch.5.7's stages are sequential and later stages (Duty, Permit, Warning)
can depend on earlier stages' outcomes (e.g. a Permit Rule needing `ControlledGoodsFlag` as set by
an HS Code Rule) — so `Facts` is mutated in place as the pipeline progresses, not just read once
at the start. This is the one deliberate divergence from "Facts assembled once, rules evaluated
against a frozen snapshot": PRD Ch.5.7's fixed stage order only makes sense if downstream stages
can see upstream outcomes.

### Explicitly out of scope for this ADR (deferred to Phase 0 Days 31-35 / later)

- Admin CRUD API for authoring Rule/RuleVersion/RuleCondition (Day 34 per ADR-0002).
- Re-Segregation triggering (PRD Ch.5.11) — an event-driven concern (`InMemoryEventBus` /
  `IEventPublisher` from ADR-0012 firing `RulesEngine.EvaluateAsync` on attribute-change events),
  not part of the evaluation pipeline itself.
- Manual Override (PRD Ch.5.12) — a separate write path that records an override `DecisionTrace`
  row (`IsOverride = true`) alongside, not instead of, the automated one; needs its own
  authorization check (Supervisor role, per ADR-0011's role policies) — deferred.
- Persistence wiring (`IRuleSetLoader`'s EF Core implementation) and DI registration
  (`AddCustomsClearanceRulesEngine()`, mirroring ADR-0012's `AddCustomsClearanceMessaging()`
  pattern) — Phase 0 Day 31-32.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Frozen `Facts` snapshot, evaluated once at pipeline start | Simpler mental model; matches "immutable FactsSnapshot" language in Day 3 §5.4 literally | PRD Ch.5.7's stages are explicitly sequential and later stages need earlier outcomes (e.g. Permit Rules depend on HS Code Rules having set `ControlledGoodsFlag`) — a frozen snapshot can't express this | Contradicts the PRD's own pipeline order; would silently produce wrong outcomes for any rule referencing a prior stage's result |
| Single `LoadAsync` call for all rules up front, filtered in-memory per stage | Fewer DB round-trips (1 vs. 8-9 per evaluation) | Loses the natural checkpoint of "what rules were even considered at this stage" for the trace; couples the loader to knowing the full category list instead of one stage at a time | Rejected for the spike design on clarity grounds; worth revisiting as a Day 31 performance optimization if evaluation latency becomes a concern — flagged, not decided against permanently |
| Priority ties resolved by explicit `RuleId` tiebreak ordering (chosen) vs. throwing on tie | Deterministic evaluation is a hard PRD Ch.5.14 acceptance criterion ("outcomes are deterministic and repeatable") | A silent tiebreak could mask a genuine rule-authoring conflict that should have been caught at authoring time | Accepted the tradeoff: correctness (determinism) at evaluation time is non-negotiable per Ch.5.14; tie *detection* belongs in the Day 34 admin API's validation, not runtime |

## Consequences

- Days 31-35 build against these five interfaces (`IFactsProvider`, `IRuleSetLoader`,
  `IConditionEvaluator`, `IPriorityResolver`, `IDecisionTraceRecorder`) plus the `RulesEngine`
  orchestrator — Day 31 = schema + `IRuleSetLoader`, Day 32 = `IConditionEvaluator` +
  `IPriorityResolver` + orchestration, Day 33 = `IDecisionTraceRecorder`, Day 34 = admin CRUD API,
  Day 35 = integration into Segregation/Compliance/Duty & Tax Engine call sites.
- The Segregation Engine, Compliance Engine, and Duty & Tax Engine (ADR-0002's shared-infrastructure
  commitment) all call `RulesEngine.EvaluateAsync` with different `SubjectType`/`categories`
  subsets — Compliance Engine likely reuses the same pipeline with a Compliance-specific stage
  subset (Permit, Warning) rather than the full Ch.5.7 order; this needs revisiting once Compliance
  Engine's own bounded-context design is scoped (not yet done as of Day 19).
- Hardcoding the PRD Ch.5.7 stage order (rather than making it configurable) is a deliberate,
  named tradeoff: the *rules* are config-driven per ADR-0002, but *pipeline stage order* is treated
  as a product/architecture decision requiring a release to change — revisit only if a future
  requirement demands per-tenant or per-jurisdiction stage reordering (none identified as of Day 19).
- A throwaway spike prototype validating this design against 3 PRD Ch.5.10 decision-table
  scenarios (FTZ, HS Code Controlled, De Minimis) was built at `spike/RulesEngineSpike/` (in-memory
  only, no persistence, not added to `CustomsClearance.sln`) and passes all 3 scenarios (exit 0,
  unpiped). It surfaced two real bugs in the design-as-implemented, both fixed in the spike: (1)
  `ApplyOutcome` must explicitly mark a "clearance type assigned" fact, or a catch-all/default rule
  (PRD's "Otherwise → CT=INF") re-fires and overwrites every earlier stage's outcome on every
  evaluation; (2) `RuleCategory` values must be distinct **per PRD Ch.5.7 stage**, not reused across
  stages that happen to share a conceptual grouping (FTZ and De Minimis were both initially mapped
  to a single `Shipment` category, causing `IRuleSetLoader.Load` to load both rules into both
  stages). Both findings are corrections to carry into the Phase 0 Day 31-32 build, not just spike
  artifacts.

## Related

[[ADR-0002]] (the config-driven decision this designs the pipeline for), [[ADR-0012]] (Persistence/
Messaging platform libraries this engine is built on), Day 3 ER Diagram v0.2 §5 (Rule/RuleVersion/
RuleCondition/DecisionTrace schema), PRD Ch.5 (Segregation Engine — Ch.5.4/5.7/5.9/5.10-5.14),
Day-Wise Plan Days 31-35 (Phase 0 build this ADR prepares for).
