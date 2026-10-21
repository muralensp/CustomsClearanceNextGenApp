# Phase 0 Follow-ups Log

Sign-off condition 2 (`Phase0_Signoff_Note.md` section 5): clear the small Phase 0 gaps before Phase 1 scope.
Run 2026-10-20 on branch `chore/phase0-followups`, from `main` at `b183573`. Not a numbered plan day.

## 1. Teardown `trap` ordering (Day 48 finding) - done

- `contract-test.sh` and `smoke-test.sh` registered their teardown `trap` after `docker compose up`, so a failed
  `up` left the stack running. Both now register it first. (`e2e-smoke-test.sh` and `perf-baseline.sh` already did.)
- `contract-test.sh` also had no retry for the RabbitMQ first-boot race (ADR-0028). It now retries `up` up to 3
  times, as `smoke-test.sh` does.
- Verified live: `contract-test.sh` exit 0. The race hit naturally on attempt 1 (`rabbitmq ... is unhealthy`), the
  retry recovered, Newman passed 3/3 assertions, and no containers from the project remained afterwards.

## 2. Rules-admin mutations audited (Gate 2 exception) - done

- New `src/Api/RulesAdmin/RuleAudit.cs` (mirrors `ConfigAudit`): `BusinessAudit`, classified Internal, acting user from
  the JWT subject. Rule changes alter clearance decisions, hence Business rather than Technical.
- `RuleAdminEndpoints.cs` now writes `RuleCreated`, `RuleUpdated`, `RuleDeactivated`, `RuleVersionAdded` after each
  successful `SaveChangesAsync`. Updates and deactivations audit only genuine changes. A superseded version's
  `EffectiveTo` change is recorded in the `RuleVersionAdded` event.
- E2E extended (section 5): 20 -> 21 checks. It updates and deactivates the rule, then asserts the audit trail for the
  rule id holds exactly 3 events with the three actions and the admin user.
- Verified live: E2E 21/21, exit 0. Fault injection: with the `src/Api/RulesAdmin` change stashed, the same script
  exits 1 at `FAIL [16] ... "total":3` (`"total":0` returned).
- Build 0 warnings, `dotnet format --verify-no-changes` exit 0, `dotnet test` exit 0 (all 13 assemblies).
- Checklist item 5.7 updated.

## 3. `docs/WORK-ITEMS.md` - deferred

Restarting it needs Phase 1 work items, which do not exist until the tenancy and identity decisions are made.

## Not done / limits

- No unit test covers the new audit calls: Api endpoint logic has no measured coverage (Day 47 exception), so the
  E2E check is the only proof.
- The historical "20/20" figures in ADR-0034/0035, the Day 48 log and the sign-off note are left as written.
