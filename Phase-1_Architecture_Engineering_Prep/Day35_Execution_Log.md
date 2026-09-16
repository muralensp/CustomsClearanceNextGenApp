# Day 35 Execution Log — Rules Engine Review

Phase 0, Week 7 (Days 31-35, Rules Engine Skeleton), Day 35 of the non-parallelizable build chain
— the final review day. Deliverable: "Load 2-3 real India-specific sample rules through the actual
admin API, verify evaluation + decision-trace output."

---

## Step 1 — Found a real gap before any rule could be usefully evaluated

Every existing evaluation entry point (`POST /diagnostics/rules/evaluate`, Day 32) only ever
supplies one hardcoded fact (`ShipmentValue`). That proves one De Minimis rule fires, but cannot
prove ADR-0013's central design claim — that `Facts` is deliberately mutable in place so a later
pipeline stage reacts to an earlier stage's outcome. Real, chaining India rules needed a way to
supply an arbitrary fact bag. Added `POST /diagnostics/rules/evaluate-facts` (`src/Api/Program.cs`)
accepting a `Dictionary<string, string>` body and running it through the real `RulesEngine`
pipeline — an addition alongside the existing endpoint, not a replacement.

## Step 2 — Brought up the real stack

`docker compose up -d --build`. First attempt hit the familiar RabbitMQ first-boot race
(`rabbitmq is unhealthy`) — the exact symptom ADR-0028/Day 34 root-caused and fixed with
`restart: on-failure:5`. A plain retried `up` succeeded immediately with zero manual
intervention, the first time this session that fix was exercised for real rather than
re-verified in isolation.

## Step 3 — Loaded three real India-specific sample rules via the actual admin API

Logged in as the seeded `admin` dev persona (`POST /auth/login`), then three real
JWT-authenticated `POST /api/rules` calls (not hand-seeded into the database):

1. **`IN-DEM-GIFT-001`** (Category: DeMinimis) — individual gift imports under INR 5000 clear via
   the De Minimis channel (India CBIC gift-import policy — the exact "threshold varies by
   channel/purpose/importer type" domain constraint named on Day 1's Bounded Context Map).
2. **`IN-HS-CONTROLLED-9018`** (Category: HSCode) — HS Code 9018 (medical/surgical/dental
   instruments) is import-restricted in India; sets `ControlledGoodsFlag`.
3. **`IN-PERMIT-CONTROLLED-001`** (Category: Permit) — any shipment already flagged as controlled
   goods requires a CDSCO import permit. Its only condition reads `ControlledGoodsFlag`, a fact no
   caller ever supplies directly — it can only become true via an earlier stage, making this rule
   the deliberate proof of cross-stage chaining.

All three returned `201` with the full nested Rule/RuleVersion/Condition graph.

## Step 4 — Evaluated two real scenarios end to end

- **Scenario A** (individual gift, value 2500, HS code `610910`): `ClearanceType: "DEM"`, trace
  step 4 (De Minimis) shows `IN-DEM-GIFT-001` as the winning rule, every other stage "no match."
- **Scenario B** (commercial shipment, value 50000, HS code `9018`): `ControlledGoodsFlag: "true"`
  at step 6 (HS Code) **and** `PermitRequired: "true"` at step 8 (Permit) — the Permit rule fired
  purely because the HS Code stage mutated Facts earlier in the same run. This is the first real
  proof of ADR-0013's mutable-Facts design, which a single-fact evaluate call could never
  demonstrate. No De Minimis match (correct — commercial, not a gift).

## Step 5 — Independently re-verified the persisted decision trace

Both evaluations' traces re-queried via the Day 33 `GET /diagnostics/rules/trace/{evaluationId}`
endpoint and via a direct `psql` count — 9 real persisted rows each, with `RuleId`/`RuleVersionId`
on the winning steps matching the actual IDs returned by the admin API's own create responses.

## Step 6 — Closed the loop across the whole week again: deactivate and re-evaluate

`POST /api/rules/{id}/deactivate` on `IN-DEM-GIFT-001`, then re-ran Scenario A — `ClearanceType`
no longer set, De Minimis stage back to "no match." Confirms Day 34's deactivate endpoint and Day
32's evaluation pipeline still compose correctly with Day 35's own sample rules layered on top.

## Step 7 — Full-solution and CI verification

`dotnet build` (clean, only the pre-existing/documented NU1902 advisory), `dotnet format
--verify-no-changes` (clean), `dotnet test` (all 9 projects passed — coverage gates unaffected,
since the only production change is an uncovered dev-only diagnostic endpoint, the same precedent
every prior day's diagnostic endpoint set). CI's actual `build/scripts/smoke-test.sh` re-run end
to end, unpiped exit code 0 — real PostgreSQL and RabbitMQ round-trips both verified.

## Outcome

**Deliverable met**: three real India-specific sample rules loaded through the actual Day 34
admin API (not hand-seeded), evaluated end to end through the real Day 32 pipeline, with output
independently confirmed via the real Day 33 persisted decision trace — including a genuine
cross-stage-chaining scenario that no prior day's proof could demonstrate. **Decision recorded**:
`adr/0029-rules-engine-review.md` (Accepted).

**Status**: Complete. Week 7 (Days 31-35, Rules Engine Skeleton) is fully complete as a
cross-validated set. Next unstarted item is whatever Phase 0 schedules next, pending explicit
instruction to begin.
