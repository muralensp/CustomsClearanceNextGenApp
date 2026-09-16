# Day 33 Execution Log — Rules Engine: Versioning & Decision Trace

Phase 0, Week 7 (Days 31-35, Rules Engine Skeleton), Day 33 of the non-parallelizable build chain.
Deliverable: "Decision trace visible for a test rule evaluation."

---

## Step 1 — Scope check: what's actually left

Versioning (`RuleVersion`, effective-dating) was already built Day 31 and consumed by Day 32's
loader. The real remaining Day 33 work, per ADR-0013/ADR-0026's own breakdown: replace Day 32's
`InMemoryDecisionTraceRecorder` placeholder with real, persisted trace capture.

## Step 2 — Build the persisted trace

- New `DecisionTrace` entity - one row per pipeline stage per evaluation, grouped by an indexed
  `EvaluationId`. `RuleId`/`RuleVersionId` nullable, `OnDelete(SetNull)` (not cascade) - a trace row
  is an audit record that must outlive the Rule/RuleVersion it referenced.
  `FactsSnapshotJson` stores the Facts bag as JSON text, not a fixed relational shape.
- `RulesDbContext` - added `DbSet<DecisionTrace>` and its fluent config.
- **Corrected a Day 32 deviation from ADR-0013's own spec**: `IDecisionTraceRecorder.Flush()` was
  sync; ADR-0013's original design always specified an async `FlushAsync`. Fixed now that a real
  implementation needs `SaveChangesAsync`. `RecordStep` also gained `evaluationId`/`context`
  parameters.
- `EfDecisionTraceRecorder` - buffers `DecisionTrace` rows into the change tracker per step, one
  `SaveChangesAsync` on `FlushAsync`.
- `RulesEngine.EvaluateAsync` - generates one `EvaluationId` per run, threads it through every
  `RecordStep` call, returns it in `SegregationResult`.
- `AddCustomsClearanceRulesEngine()` - switched the `IDecisionTraceRecorder` DI binding to
  `EfDecisionTraceRecorder`.
- New `GET /diagnostics/rules/trace/{evaluationId}` endpoint - proves the trace is genuinely
  queryable back out of Postgres, not just returned in-process by the evaluate call.

## Step 3 — Coverage gate

First run: 87.67% (new untested code: `EfDecisionTraceRecorder`, `DecisionTrace`'s Day-33-specific
config paths). Added `EfDecisionTraceRecorderTests` (5 tests: per-step persistence, null IDs on
no-match, real IDs on a win, JSON snapshot content, two evaluations independently queryable).
Reached 100% line / 97.75% branch - 50 tests total in the project.

## Step 4 — Live verification against real PostgreSQL (the literal deliverable)

`POST /diagnostics/rules/evaluate?shipmentValue=2000` returned a real `evaluationId`. `GET
/diagnostics/rules/trace/{evaluationId}` returned **9 real persisted rows**, correctly ordered,
winning stage showing real `RuleId`/`RuleVersionId`, and `FactsSnapshotJson` correctly showing
`ClearanceType` appearing in the snapshot from stage 5 onward - proving the cross-stage Facts
mutation itself was captured in the persisted trace, not just the in-process result. Direct `psql`
query confirmed 18 rows (two evaluations x 9 stages) and the real `ON DELETE SET NULL` FK
constraints on `DecisionTraces`.

## Step 5 — A different RabbitMQ failure this time, root-caused to a corrupted image

The familiar `.erlang.cookie: eacces` crash recurred - but this time against a **provably fresh**
anonymous volume (inspected directly: the cookie file was already root-owned/read-only inside a
brand-new volume, ruling out stray leftover state). Restarting Docker Desktop
(`wsl --shutdown` + relaunch) did not fix it. Root-caused to a corrupted local copy of the
`rabbitmq:3.13-management-alpine` image - `docker rmi -f` + `docker pull` (fresh image) resolved it
permanently, confirmed via a clean `build/scripts/smoke-test.sh` run and a second live
evaluate/trace round trip. Documented as a distinct root cause from the Days 26/27/30 stray-volume
findings - same symptom, different underlying problem this time.

## Step 6 — Final clean-up verification

Full-solution `dotnet test` (9 test projects, unpiped exit 0), `dotnet format
--verify-no-changes` (clean). `docker compose down -v`, then `build/scripts/smoke-test.sh` -
unpiped exit 0.

## Outcome

**Deliverable met**: a decision trace for a test rule evaluation is genuinely visible - persisted
in PostgreSQL, independently queryable by `EvaluationId`, showing every pipeline stage including
no-match steps, with the winning rule's identity and the Facts snapshot at each point. **Decision
recorded**: `adr/0027-rules-engine-versioning-and-decision-trace.md` (Accepted).

**Status**: Complete. Per the Day-Wise Plan's strict build chain, **Day 34 — Rules Engine: Admin
API (minimal)** is next, pending explicit instruction to begin.
