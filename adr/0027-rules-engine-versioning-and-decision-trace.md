# ADR-0027: Rules Engine - Versioning & Decision Trace (Day 33)

**Status:** Accepted
**Date:** 2026-09-16
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 7 (Days 31-35, Rules Engine Skeleton), Day 33 - third day of the non-parallelizable
build chain. Task: "Implement rule versioning and decision-trace capture (per PRD Ch.5
explainability requirement)." Deliverable: "Decision trace visible for a test rule evaluation."

Versioning itself (`RuleVersion` - effective-dated, `EffectiveFrom`/`EffectiveTo`) was already built
on Day 31 (ADR-0025) and consumed by Day 32's `EfRuleSetLoader`. Day 33's real remaining scope,
exactly as ADR-0013/ADR-0026 flagged it, is replacing Day 32's `InMemoryDecisionTraceRecorder`
placeholder with a real, persisted implementation - PRD Ch.5.13's explainability requirement
("every rule that fired, in order, with the facts it saw") only means something if the trace
outlives the request that produced it.

## Decision

1. **New `DecisionTrace` entity** (`AuditableEntity<Guid>`) - one row per PRD Ch.5.7 pipeline stage
   per evaluation run. `EvaluationId` (Guid) groups every row from one `RulesEngine.EvaluateAsync`
   call - indexed, so "the trace for evaluation X" is a real index scan. `RuleId`/`RuleVersionId`
   are nullable (a "no match" step records no winner, per PRD Ch.5.13) and configured
   `OnDelete(DeleteBehavior.SetNull)` rather than cascading from `Rule`/`RuleVersion` - a trace row
   is an audit record (ADR-0004) that must survive independent of the rule data it referenced, even
   though Rules are soft-deactivated, never hard-deleted, per ADR-0025 (this is defense-in-depth for
   that invariant, not a substitute). `FactsSnapshotJson` stores the Facts bag as JSON text, not a
   fixed relational shape - Facts is an open, evaluation-time-variable key set (PRD Ch.5.4), so a
   column-per-fact schema would need a migration for every new fact name.
2. **`IDecisionTraceRecorder.Flush()` -> `FlushAsync(CancellationToken)`** - corrects a Day 32
   deviation from ADR-0013's own original interface spec (which always specified an async
   `FlushAsync`; Day 32's in-memory implementation didn't need I/O yet, so used a sync `Flush()`).
   `RecordStep` also now takes `Guid evaluationId` and `EvaluationContext context`, needed by the
   real implementation to populate `DecisionTrace`'s `EvaluationId`/`SubjectId`/`SubjectType`/
   `EvaluationDate` columns.
3. **`EfDecisionTraceRecorder`** - the real implementation. Buffers one `DecisionTrace` entity per
   `RecordStep` call into the change tracker; `FlushAsync` issues one `SaveChangesAsync` (one round
   trip per evaluation, not one per step). Scoped in DI, so one recorder - and one evaluation's
   worth of buffered rows - lives per `RulesEngine.EvaluateAsync` call.
4. **`RulesEngine.EvaluateAsync`** now generates one `EvaluationId` (`Guid.NewGuid()`) at the start
   of each run, threads it through every `RecordStep` call, and returns it as part of
   `SegregationResult` - the key a caller uses to re-query the persisted trace afterward.
5. **`AddCustomsClearanceRulesEngine()`** now registers `EfDecisionTraceRecorder`, replacing Day
   32's `InMemoryDecisionTraceRecorder` binding - exactly the "Day 33 adds an EF-backed
   implementation without changing this interface or RulesEngine's orchestration" plan ADR-0013/
   ADR-0026 both stated, other than the `Flush` -> `FlushAsync` correction above.
6. **New diagnostic endpoint** `GET /diagnostics/rules/trace/{evaluationId}` - proves the trace is
   genuinely queryable back out of PostgreSQL by `EvaluationId`, independent of the in-process
   result the evaluate call already returns; the query shape a real future compliance-review screen
   would use.

## Verification

Unit tests (EF Core InMemory provider, ADR-0009): `EfDecisionTraceRecorderTests` - one row
persisted per step, null `RuleId`/`RuleVersionId` on a no-match step, correct IDs on a winning
step, Facts snapshot serialized as JSON, two concurrent evaluations' rows independently queryable
by `EvaluationId`. 50 tests total (up from Day 32's 45), 100% line / 97.75% branch coverage against
the ADR-0007 95% core-tier gate.

**Live verification against real PostgreSQL - the literal deliverable**: `POST
/diagnostics/rules/evaluate?shipmentValue=2000` returned a real `evaluationId`; `GET
/diagnostics/rules/trace/{evaluationId}` returned all **9 real persisted rows**, correctly ordered
by `StepOrder`, with the winning De Minimis stage showing real `RuleId`/`RuleVersionId` values and
`FactsSnapshotJson` correctly showing `ClearanceType` appearing in the snapshot from stage 5
onward (proving the cross-stage Facts mutation itself was captured in the persisted trace, not
just in the in-process result). Direct `psql` inspection confirmed 18 real rows (two test
evaluations x 9 stages) and the real FK constraints (`ON DELETE SET NULL`, as designed) on
`DecisionTraces`. Full-solution `dotnet build`/`dotnet format --verify-no-changes`/`dotnet test`,
all clean with unpiped exit codes.

### Real environmental issue found and fixed (not a design bug, different root cause than Days 26-30/32)

The first several `docker compose up` attempts this day hit the familiar `.erlang.cookie: eacces`
RabbitMQ crash - but this time it recurred even against a **genuinely fresh** anonymous volume
(confirmed via direct inspection: `.erlang.cookie` inside a brand-new volume was already
root-owned and read-only, unreadable by the container's own `rabbitmq` user). Ruled out stray
Docker state (the prior root cause on Days 26/27/30) since the volume was provably new. Restarting
Docker Desktop (`wsl --shutdown` + relaunch) did not fix it either. Root-caused to a corrupted
local copy of the `rabbitmq:3.13-management-alpine` image itself - removing it (`docker rmi -f`)
and re-pulling a fresh copy (`docker pull`) resolved it permanently; re-verified via a full
`build/scripts/smoke-test.sh` pass (unpiped exit 0) and a second live evaluation/trace round trip.
Documented as a distinct root cause from the prior days' stray-volume findings, for future
reference if this symptom recurs again.

## Consequences

- Day 34 (Admin API) can now surface a rule's evaluation history (via `EvaluationId`/`RuleId`
  lookups against `DecisionTraces`) as a real feature, not a stub - the persistence this ADR adds is
  what makes that possible.
- `IDecisionTraceRecorder`'s `Flush`-to-`FlushAsync` correction is a reminder that a Day N
  placeholder's own signature can still drift from the original design spec in small ways under
  pressure to "just make it compile" - worth re-checking against the source ADR when the real
  implementation lands, not just assuming the placeholder got it right.
- `DecisionTrace` has no admin/query API of its own yet beyond this day's diagnostic endpoint -
  Day 34 is where a real, authorization-gated trace-lookup endpoint belongs.

## Related

[[ADR-0026]] (Day 32 - the evaluation pipeline and `InMemoryDecisionTraceRecorder` placeholder this
ADR replaces), [[ADR-0025]] (Day 31 - the schema `DecisionTrace` extends), [[ADR-0013]] (Day 19
design spike - the original async `FlushAsync` signature this day restores), [[ADR-0004]]
(Security & Audit standards - the audit-record-survives-source-data reasoning behind `SetNull`
rather than cascade delete), Day-Wise Plan Days 31-35 (Rules Engine Skeleton, non-parallelizable
build chain).
