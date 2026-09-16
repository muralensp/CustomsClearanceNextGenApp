# ADR-0026: Rules Engine - Evaluation Pipeline (Day 32)

**Status:** Accepted
**Date:** 2026-09-16
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 7 (Days 31-35, Rules Engine Skeleton), Day 32 - second day of the non-parallelizable
build chain. Task: "Implement rule evaluation service (input facts -> matched rules -> ordered by
priority -> outcome)." Deliverable: "Evaluation service passes unit tests on dummy rules."

Day 31 (ADR-0025) built the Rule/RuleVersion/RuleCondition schema. This day builds the evaluation
pipeline ADR-0013 (Day 19 design spike) already specified against that schema:
`IRuleSetLoader -> IConditionEvaluator -> IPriorityResolver -> RulesEngine` orchestration
(`IDecisionTraceRecorder`'s real, persisted implementation is explicitly Day 33's scope per
ADR-0013's own breakdown - this day uses an in-memory one, good enough to prove the pipeline).

## Decision

1. **`Facts`** - a mutable, string-keyed fact bag (unchanged in shape from the Day 19 spike),
   mutated in place as the pipeline progresses so later stages see earlier stages' outcomes
   (ADR-0013's `ApplyOutcome`). **`EvaluationContext`**/`SubjectType` carry what's being evaluated
   and when - `IFactsProvider`'s real, per-subject-type implementation is deliberately **not**
   built yet, since no real Shipment/Declaration bounded context exists to assemble Facts from;
   `Facts` is supplied directly to `RulesEngine.EvaluateAsync` by the caller for now.
2. **`PipelineStages.Ordered`** - PRD Ch.5.7's fixed 9-stage order, hardcoded per ADR-0013 (stage
   order is a product/architecture decision, not rule-authoring policy), mapped onto Day 31's
   `RuleCategory` enum with `DeMinimis` as its own stage (not folded into `Shipment` - the Day 19
   spike's own finding that categories must stay distinct per stage still applies).
3. **`EfRuleSetLoader`** - the real EF Core implementation of `IRuleSetLoader` against
   `RulesDbContext`: `Rule.Active == true AND Category IN categories`, then in-memory selection of
   the one `RuleVersion` whose effective window contains the evaluation date (`EffectiveFrom <=
   date AND (EffectiveTo IS NULL OR EffectiveTo >= date)`) - `SingleOrDefault`, so an authoring bug
   that leaves two overlapping versions throws immediately rather than silently picking one.
4. **`ConditionEvaluator`** - implements all 6 operators ADR-0013 named (the Day 19 spike only had
   4: Equals/NotEquals/GreaterThan/LessThan). Normalizes a Fact's boxed runtime value and a
   condition's always-text `Value` to a common comparable type (decimal for numerics, `DateOnly`
   for dates) before comparing, so `GreaterThan`/`LessThan`/`Between` compare by magnitude, not
   lexical text (`"10" < "9"` under plain string ordering would be wrong). Falls back to ordinal
   string comparison when neither side is numeric/date-shaped.
5. **`PriorityResolver`** - lowest `Rule.Priority` number wins (PRD Ch.5.9), ties broken
   deterministically by `RuleCode` (ordinal) - a data-authoring-conflict case surfaced as a
   repeatable, not crashing, outcome (PRD Ch.5.14's determinism requirement), per ADR-0013.
6. **`RulesEngine.EvaluateAsync`** orchestrates all 9 stages: load candidates for the stage's
   categories, filter to those whose every condition is satisfied, resolve the single winner,
   record a trace step (even on no-match, per PRD Ch.5.13), then mutate `Facts` with the winner's
   `OutcomeFactName`/`OutcomeValue` before moving to the next stage.
7. **`InMemoryDecisionTraceRecorder`** - Day 32's placeholder for `IDecisionTraceRecorder`, same
   shape as the Day 19 spike's. Day 33 swaps in a real EF-backed implementation without changing
   this interface or `RulesEngine`'s constructor.
8. **`AddCustomsClearanceRulesEngine()`** registers the whole pipeline behind the same
   `RulesDb`-presence guard as the schema itself; wired into the `Api` host the same way every
   other `AddCustomsClearance*` library is.

## Verification

Unit tests (EF Core InMemory provider for `EfRuleSetLoaderTests`, pure logic for
`ConditionEvaluatorTests`/`PriorityResolverTests`, an in-memory `IRuleSetLoader` stub for
`RulesEngineTests` - the literal Day 32 deliverable, "passes unit tests on dummy rules") - 45 tests
total, 100% line / 97.53% branch coverage against the ADR-0007 95% core-tier gate. Notably proven:
a rule set spanning two different pipeline stages where a later stage's condition reads a fact an
earlier stage set (`ControlledGoodsFlag`), and a same-stage priority conflict resolved correctly.

**Live verification against real PostgreSQL** (not just InMemory-provider unit tests): a new
`POST /diagnostics/rules/evaluate` endpoint seeds one real De Minimis rule via `RulesDbContext` if
absent, then runs `RulesEngine.EvaluateAsync` against it for a supplied `shipmentValue`. A
shipment value under the threshold produced `clearanceType: "DEM"` with the De Minimis stage's
trace step showing the winning `RuleCode`; a value over threshold produced `clearanceType: null`
with every stage showing "no match" - both against the real rule row, confirmed still present via
direct `psql` query, loaded through `EfRuleSetLoader`'s real query, not an in-memory stub. Full
9-stage trace returned in both cases, confirming `PipelineStages.Ordered`'s full sequence executes
even when most stages have no applicable rules. Full-solution `dotnet build`/`dotnet format
--verify-no-changes`/`dotnet test`, all clean with unpiped exit codes (0/0/0). CI's
`build/scripts/smoke-test.sh` re-run clean.

**Recurring environmental issue, same category as Days 26-30 (not a design bug)**: the first
`docker compose up` of this day hit the same `.erlang.cookie: eacces` RabbitMQ crash, this time
traced to a stale volume belonging to a previous run's crashed `rabbitmq` container itself (not an
unrelated orphan, unlike Day 27/30's findings) - removed the container and its volume together;
the very next `up` started clean. Same inspect-don't-blindly-prune discipline as prior days.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Load all rules for all 9 stages in one query up front, filter in-memory per stage | Fewer DB round-trips | ADR-0013 already flagged this as a Day 31/32 optimization to revisit only if evaluation latency becomes a real concern - no evidence of that yet at this scale | Deferred, per ADR-0013's own note, not rejected outright |
| Build a real `IFactsProvider` now (e.g. against a placeholder Shipment table) | Would make Day 32 "more complete" | No real Shipment/Declaration bounded context exists yet to assemble Facts from; a placeholder Facts provider would be throwaway scaffolding, the same category of premature work ADR-0013 explicitly deferred | Rejected - Facts is supplied directly by the caller until a real bounded context exists |

## Consequences

- Day 33 (Versioning & Decision Trace) replaces `InMemoryDecisionTraceRecorder` with a real,
  persisted `DecisionTrace` EF entity/table - `IDecisionTraceRecorder`'s interface and
  `RulesEngine`'s orchestration need no change for that swap.
- Day 34 (Admin API) is the first real consumer that needs `IRuleSetLoader`'s data invariant (at
  most one effective `RuleVersion` per `Rule` per date) to hold - `EfRuleSetLoader`'s
  `SingleOrDefault` will start throwing the moment that invariant is violated by authored data,
  which is the intended fail-fast behavior, not a bug to guard against here.
- `ConditionEvaluator`'s numeric/date normalization is now the one place typed comparison logic
  lives - any future new operator (should one ever be needed) extends this class, not the schema.
- No real `IFactsProvider` exists yet - any bounded context wanting to call `RulesEngine
  .EvaluateAsync` today must assemble its own `Facts` and call it directly, same as this day's
  diagnostic endpoint does.

## Related

[[ADR-0025]] (Day 31 - the schema this pipeline evaluates against), [[ADR-0013]] (Day 19 design
spike - the five-interface design this day implements four of), [[ADR-0002]] (config-driven
decision), Day-Wise Plan Days 31-35 (Rules Engine Skeleton, non-parallelizable build chain).
