# ADR-0034: Phase 0 End-to-End Smoke Test (Day 40)

**Status:** Accepted
**Date:** 2026-10-18
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 8 (Days 36-40), Day 40 - the single sequential verification pass that closes the
Configuration & Notification week. Task: "End-to-end smoke test: auth -> gateway -> sample service ->
rules engine -> audit -> notification." Deliverable: "All foundation pieces verified working
together." Until now each piece was proven in isolation on its own day, and `smoke-test.sh` holds the
per-feature CI checks. Day 48 (Regression Pass) is planned to re-run "the full smoke/integration suite
from Day 40", so this pass needs to be a re-runnable artefact, not a one-off manual session.

## Decision

Design questions were settled with the user before building (the first round was returned for
clarification; the reporting questions were then asked again on their own):

1. **Verify-only chain.** No product code is added. The script exercises the existing pieces in
   order, and nothing newly links them: the rules engine, the audit trail and the notification
   service remain independent capabilities. (Adding glue so a single business action drives all three
   was offered and not chosen.)
2. **A new script and its own CI job**, not an extension of `smoke-test.sh`:
   `build/scripts/e2e-smoke-test.sh`, run by a new `e2e-smoke-test` job in `.github/workflows/ci.yml`
   and an `E2ESmokeTest` stage in `build/azure-pipelines.yml` (lockstep, ADR-0005). The existing
   per-feature smoke test is unchanged.
3. **Happy path only.** Negative/security cases (auth matrix, wrong role), email-failure isolation and
   the deactivated-rule case are covered by the per-day tests and execution logs, not re-done here.
4. **Reporting**: a numbered `PASS [nn]` / `FAIL [nn]` line per check, a final summary, and an
   evidence file (`TestResults/e2e-evidence.txt`) holding the run id, git commit, correlation ID and
   every ID the run touched (admin user, widget + event, rule + evaluation, config entry, notification
   event + notification, deliveries) and a `result=PASS|FAIL` line. CI uploads it as an artifact.
5. **Stop at the first failure and dump container logs.** The chain is sequential, so later steps
   would only fail because an earlier one did; one failure gives one clear cause.

**What the 20 checks cover** (all through the Gateway on :8080, one `X-Correlation-Id` on every
request):

| Stage | Checks |
|---|---|
| Environment | `docker compose up --build` (with the RabbitMQ first-boot retry) and health |
| Auth | admin and broker log in; JWT carries role Admin |
| Gateway | own liveness; `/health` proxied; Admin JWT accepted by *both* the Gateway policy and the Api policy on `/diagnostics/secure`; our correlation ID echoed |
| Sample service | widget written to and read back from PostgreSQL; its event published, consumed via RabbitMQ and handled **with our correlation ID** |
| Rules engine | rule authored via the Admin API; `ShipmentValue=2500` evaluates to `ClearanceType=DEM` naming that rule; 9 decision-trace rows persisted referencing the rule's id |
| Audit | config created then changed 5000 -> 10000; audit trail holds both events with old/new value, timestamp, `performedBy` = the admin's `Users.Id`, and our correlation ID |
| Notification | `NotificationRequested` published (log line carries our correlation ID); handler persisted the notification with `Email=Delivered,InApp=Delivered`; the broker sees it via `/api/notifications/mine`; the email arrived at the SMTP server (Mailpit) |

## Verification

- **Pass**: run twice from a clean `docker compose down -v` state on the unmodified compose file: 20/20
  checks, exit 0 both times (evidence embedded in `Day40_Execution_Log.md`).
- **The harness can fail**: because it passed first time, a failure was injected on purpose (pointing the
  script at a dead SMTP-inbox address). It reported `FAIL [20]`, stopped there with 19 passes, dumped
  the container logs, wrote `result=FAIL` and the failing check to the evidence file, and exited 1;
  teardown still ran.
- Both pipeline YAML files parse; `bash -n` is clean.
- No product code changed, so the build/format/unit/integration results from Day 39 stand.
- The new CI job has **not yet run on GitHub** at the time of writing; its first result comes with the
  push.

## Consequences

- Day 48 can re-run this script as the regression pass; any later phase can extend it with a stage.
- **What "end-to-end" does and does not mean here.** It proves each foundation piece works through the
  real edge, on the real stack, and that a correlation ID survives the Gateway, the event bus, the
  audit store and the publish log. It does **not** prove a single business action flows through the
  rules engine into audit and notification, because that wiring does not exist yet. The correlation
  ID is also *not* carried onto the decision-trace rows or the notification record (the notification is
  tied to its event by `SourceEventId`), so correlation is verified at the points listed above, not
  everywhere.
- Observability (logs/metrics/traces/alerts, Days 26-30) and the Day 37-39 negative behaviours are not
  re-verified here.
- The script uses the seeded dev personas' passwords (the same dev-only values as `smoke-test.sh`);
  they exist only in the local/CI compose environment.
- A run takes roughly three minutes, dominated by building the images.

## Related

[[ADR-0005]] (CI lockstep), [[ADR-0008]] (compose-based CD smoke), [[ADR-0009]] (test layering - this is
the real-infrastructure layer), [[ADR-0016]]/[[ADR-0019]] (Gateway and its JWT enforcement),
[[ADR-0017]] (Identity), [[ADR-0018]] (event bus), [[ADR-0026]]-[[ADR-0029]] (rules engine),
[[ADR-0030]] (config), [[ADR-0031]] (notifications), [[ADR-0032]] (audit trail).
