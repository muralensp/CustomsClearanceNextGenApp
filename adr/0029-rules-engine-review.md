# ADR-0029: Rules Engine Review (Day 35)

**Status:** Accepted
**Date:** 2026-09-16
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 7 (Days 31-35, Rules Engine Skeleton), Day 35 - final, review day of the
non-parallelizable build chain. Task: "Load 2-3 real India-specific sample rules through the
actual admin API (not hand-seeded), verify evaluation + decision-trace output." This is the
integration proof for the whole week: Day 31 (storage), Day 32 (evaluation pipeline), Day 33
(persisted decision trace), Day 34 (admin API) each proved their own slice in isolation; Day 35
proves they work together the way a real compliance admin would actually use them.

One real gap found before any rules could be loaded: every existing evaluation entry point
(`POST /diagnostics/rules/evaluate`, Day 32) only ever supplied a single hardcoded fact
(`ShipmentValue`). That is enough to exercise one De Minimis rule, but not enough to prove
ADR-0013's central design claim - that `Facts` is deliberately mutable in place so a later
pipeline stage (e.g. Permit) can react to an earlier stage's outcome (e.g. HS Code setting
`ControlledGoodsFlag`). Real India-specific rules that actually chain across stages needed a way
to supply an arbitrary fact bag.

## Decision

1. **New diagnostic endpoint**: `POST /diagnostics/rules/evaluate-facts` (`src/Api/Program.cs`) -
   accepts an arbitrary `Dictionary<string, string>` request body, builds a `Facts` bag from it,
   and runs the same real `RulesEngine.EvaluateAsync` pipeline Day 32 built, against whatever
   rules are actually loaded in PostgreSQL. The existing single-fact `evaluate` endpoint (Day 32)
   is left unchanged - this is an addition, not a replacement, following the same
   dev-only-diagnostic-endpoint pattern as every prior day's proof endpoint.
2. **Three real India-specific sample rules, loaded through the actual Day 34 admin API** (not
   hand-seeded directly into the database, per the deliverable's own wording):
   - `IN-DEM-GIFT-001` (Category: DeMinimis) - individual gift imports under INR 5000 clear via
     the De Minimis channel, reflecting India's CBIC gift-import policy (the exact "de minimis
     threshold varies by import channel/purpose/importer type" domain constraint named back on
     Day 1's Bounded Context Map).
   - `IN-HS-CONTROLLED-9018` (Category: HSCode) - HS Code 9018 (medical/surgical/dental
     instruments) is import-restricted in India and sets `ControlledGoodsFlag`.
   - `IN-PERMIT-CONTROLLED-001` (Category: Permit) - any shipment already flagged as controlled
     goods by an earlier stage requires a CDSCO import permit. This rule's only condition reads a
     fact (`ControlledGoodsFlag`) that no caller ever supplies directly - it can only be true if
     an earlier pipeline stage set it, making it the deliberate proof of cross-stage chaining.

## Verification

All against the real running system (`docker compose up`, real PostgreSQL, real JWT-authenticated
admin calls) - not assumed from Day 31-34's own isolated proofs:

- **Rules loaded via the real admin API**: three `POST /api/rules` calls (Bearer token from a
  real `admin` login), each returning `201` with the full nested Rule/RuleVersion/Condition graph.
- **Scenario A** (individual gift, value 2500, non-controlled HS code `610910`): evaluated via
  the new `evaluate-facts` endpoint -> `ClearanceType: "DEM"`, trace step 4 (De Minimis) shows
  `IN-DEM-GIFT-001` as the winning rule, every other stage `no match`.
- **Scenario B** (commercial shipment, value 50000, HS code `9018`): -> `ControlledGoodsFlag:
  "true"` at step 6 (HS Code) **and** `PermitRequired: "true"` at step 8 (Permit) - the Permit
  rule fired purely because an earlier stage mutated Facts, exactly as ADR-0013 designed and
  exactly what a single-fact evaluate endpoint could never have proven. No De Minimis match (correct
  - commercial, not a gift).
- **Persisted decision trace, independently queried**: both evaluations' traces re-queried via
  the Day 33 `GET /diagnostics/rules/trace/{evaluationId}` endpoint and via a direct `psql` count
  - 9 real persisted rows each, `RuleId`/`RuleVersionId` on the winning steps matching the actual
  seeded rule/version IDs from the admin API's own create responses.
- **Deactivation loop, closing across the whole week again**: `POST
  /api/rules/{id}/deactivate` on `IN-DEM-GIFT-001`, then re-ran Scenario A - `ClearanceType` no
  longer set, De Minimis stage back to `no match`. Confirms Day 34's deactivate endpoint and Day
  32's evaluation pipeline still compose correctly with a fifth day's worth of code layered on top.
- **RabbitMQ first-boot race**: recurred once on the very first `docker compose up` of this
  session (`rabbitmq is unhealthy`, the exact ADR-0028 symptom) - the `restart: on-failure:5`
  policy self-healed it, and a plain retried `up` succeeded with no manual intervention, the first
  time this session Day 34's fix was exercised for real rather than just re-verified in isolation.
- **Full-solution regression**: `dotnet build` (clean, only the pre-existing/documented NU1902
  advisory), `dotnet format --verify-no-changes` (clean), `dotnet test` (all 9 projects passed,
  coverage gates unaffected since the only production change is an uncovered dev-only diagnostic
  endpoint, consistent with every prior day's diagnostic-endpoint precedent). CI's actual
  `build/scripts/smoke-test.sh` re-run end to end, unpiped exit code 0 (PostgreSQL and RabbitMQ
  round-trips both verified).

## Consequences

- Days 31-34's Rules Engine Skeleton is now proven as one working system against realistic,
  India-specific policy - not just as four independently-correct pieces.
- The cross-stage-chaining proof (Scenario B) is the first real, end-to-end demonstration of
  ADR-0013's most load-bearing design decision (mutable-in-place `Facts`); any future
  `IFactsProvider` implementation (Phase 3+, once a real bounded context exists to assemble Facts
  from a shipment) inherits this same, now-proven chaining behavior for free.
- `POST /diagnostics/rules/evaluate-facts` remains a dev-only diagnostic endpoint, not a public
  API - a real evaluation entry point for a bounded context (e.g. `POST
  /declarations/{id}/evaluate`) is out of scope until Phase 3+, when a real `IFactsProvider` exists
  to assemble Facts from actual shipment/declaration data instead of a caller-supplied dictionary.
- Week 7 (Days 31-35, Rules Engine Skeleton) is complete. Per the Day-Wise Plan, this closes
  Phase 0's Rules Engine build chain; the next unstarted item is whatever Phase 0 schedules next,
  pending explicit instruction to begin.

## Related

[[ADR-0025]] (Day 31 - storage model these sample rules were persisted against), [[ADR-0026]]
(Day 32 - the evaluation pipeline proven here with a real multi-fact scenario), [[ADR-0027]] (Day
33 - the persisted decision trace independently re-queried here), [[ADR-0028]] (Day 34 - the
actual admin API these sample rules were loaded through, and whose RabbitMQ fix was exercised live
again this day), [[ADR-0013]] (the mutable-Facts/cross-stage-chaining design this day is the first
real proof of), [[ADR-0002]] (the configuration-driven rules engine decision this whole week
exists to build out).
