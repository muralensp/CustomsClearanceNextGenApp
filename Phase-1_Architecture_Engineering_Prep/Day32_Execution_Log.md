# Day 32 Execution Log — Rules Engine: Evaluation Pipeline

Phase 0, Week 7 (Days 31-35, Rules Engine Skeleton), Day 32 of the non-parallelizable build chain.
Deliverable: "Evaluation service passes unit tests on dummy rules."

---

## Step 1 — Build the pipeline against ADR-0013's design and Day 31's schema

Added to `Platform.RulesEngine`:
- `Facts`/`EvaluationContext`/`SubjectType` - the pipeline's input shape.
- `PipelineStages.Ordered` - PRD Ch.5.7's fixed 9-stage order, mapped onto Day 31's `RuleCategory`
  enum.
- `EfRuleSetLoader` - real `IRuleSetLoader` implementation against `RulesDbContext` (Active +
  Category filter, then effective-dating selection per ADR-0013's query shape).
- `ConditionEvaluator` - all 6 ADR-0013 operators (Day 19 spike only had 4), with numeric/date-aware
  comparison so `GreaterThan`/`LessThan`/`Between` compare by magnitude, not text.
- `PriorityResolver` - lowest-Priority-wins, RuleCode tiebreak.
- `InMemoryDecisionTraceRecorder` - Day 32's placeholder (real persistence is Day 33's scope).
- `RulesEngine.EvaluateAsync` - the 9-stage orchestration, mutating Facts in place across stages.
- `AddCustomsClearanceRulesEngine()` - DI wiring, same convention as every other Platform library.

Wired into `Api`'s `Program.cs` behind the existing `RulesDb`-presence guard.

## Step 2 — Coverage gate found real gaps, not just a number to hit

First `dotnet test` run: 92.79% line coverage against RulesEngine.Tests' 95% gate (ADR-0007
core-tier) - failed for real, not a formality. Inspected the cobertura report directly to find
exactly which lines were uncovered, rather than adding tests blindly:
- `ConditionEvaluator`'s fallback branches (non-numeric/non-date comparisons, malformed
  condition values, the unreachable-via-real-enum-values `NotSupportedException` default case) -
  added targeted tests for each, including one that casts an out-of-range int to
  `ConditionOperator` specifically to exercise that default case.
- `RuleVersion.Rule`/`RuleCondition.RuleVersion` inverse navigation setters - not reliably
  attributed to coverage via EF Core's automatic relationship fixup; added a direct test setting
  them explicitly.
- `ServiceCollectionExtensions.cs` - entirely untested (unlike Day 27's `Platform.Observability`,
  which has no dedicated test project at all, `Platform.RulesEngine` does, so its DI wiring is
  in-gate). Added a test resolving every registered service from a real `ServiceCollection`.

Re-ran: **100% line / 97.53% branch**, exit 0.

## Step 3 — Live verification against real PostgreSQL

Fresh `docker compose down -v` / `up -d --build`. New `POST /diagnostics/rules/evaluate?
shipmentValue=X` endpoint: seeds one real De Minimis rule via `RulesDbContext` if absent, then runs
the actual `RulesEngine` against it.

1. `shipmentValue=2000` (under the 5000 threshold) - `clearanceType: "DEM"`, full 9-step trace with
   the De Minimis stage showing the winning `RuleCode` and `"ClearanceType = DEM"`.
2. `shipmentValue=999999` (over threshold) - `clearanceType: null`, every stage "no match".
3. Direct `psql` query confirmed the seeded rule genuinely persisted (`RuleCode`/`Category`/
   `Priority` all correct) - the evaluation ran against a real Postgres-loaded rule via
   `EfRuleSetLoader`'s real query, not an in-memory stub.

## Step 4 — Recurring RabbitMQ startup issue, resolved fast

Same `.erlang.cookie: eacces` crash as Days 26-30, this time traced to a stale volume belonging to
a previous crashed `rabbitmq` container itself (not an unrelated orphan) - removed the container
and its volume together via `docker compose rm -f rabbitmq` + `docker volume rm`; the next `up`
started clean.

## Step 5 — Final clean-up verification

Full-solution `dotnet test` (9 test projects, all passing, unpiped exit 0), `dotnet format
--verify-no-changes` (clean). `docker compose down -v`, then `build/scripts/smoke-test.sh` (the
actual CI script) - unpiped exit 0, Postgres/RabbitMQ round-trip checks (Days 18/24) unaffected.

## Outcome

**Deliverable met**: the evaluation service passes unit tests on dummy rules (45 tests, the
literal Day 32 wording), and was additionally proven against a real Postgres-loaded rule end to
end - matching and non-matching cases both correct, full stage-by-stage trace returned in both.
**Decision recorded**: `adr/0026-rules-engine-evaluation-pipeline.md` (Accepted).

**Status**: Complete. Per the Day-Wise Plan's strict build chain, **Day 33 — Rules Engine:
Versioning & Decision Trace** (real EF-backed `IDecisionTraceRecorder`, replacing today's in-memory
placeholder) is next, pending explicit instruction to begin.
