# Day 40 Execution Log — Integration Smoke Test

Phase 0, Week 8 (Days 36-40), Day 40. Deliverable: "All foundation pieces verified working together."

---

## Step 1 — Checked the starting point

Before building on Days 37-39, checked GitHub Actions: all six most recent CI runs (`4764f56` through
`3f14cf9`, Days 34-39) completed **success**, including the Day 37-39 pushes that added the Mailpit,
audit and reference-data smoke checks.

## Step 2 — Settled the design with the user

The first set of design questions was returned by the user for clarification (nothing was built); the
two open reporting questions were then asked again on their own. Decisions: verify-only chain (no
glue code), new `e2e-smoke-test.sh` + its own CI job, happy path only, numbered PASS/FAIL log + a
saved evidence file, stop at first failure and dump container logs. Recorded in
`adr/0034-phase-0-end-to-end-smoke-test.md`.

## Step 3 — Read the real contracts before writing the script

Read the rule-admin request model, the `evaluate-facts` and trace endpoints, the correlation-ID
middleware (a client-supplied `X-Correlation-Id` is honoured) and which log lines carry a
correlation ID ("Published event … CorrelationId=…" for every event; "HANDLED event … CorrelationId=…"
for the widget event). That determined which correlation claims the script can truthfully assert.

## Step 4 — Built the script and the CI wiring

`build/scripts/e2e-smoke-test.sh`: 20 numbered checks across Environment, Auth, Gateway, Sample
service, Rules engine, Audit, Notification (see the ADR for the table). Explicit checks instead of
`set -e`, so every failure goes through one `fail()` path (logs, evidence, exit 1). CI: new
`e2e-smoke-test` job in `.github/workflows/ci.yml` (uploads the evidence artifact) and an
`E2ESmokeTest` stage in `build/azure-pipelines.yml` (`PublishPipelineArtifact`), kept in lockstep. Both
files validated as YAML.

## Step 5 — Ran it for real

- **Run 1**: clean `docker compose down -v`, then the script: **20/20 PASS, exit 0**.
- **Fault injection** (the script had passed first time, so it needed to be shown it can fail):
  `E2E_MAILPIT_URL=http://localhost:9` → `FAIL [20] Timed out waiting for: the email to reach the SMTP
  server (Mailpit)`, stopped with 19 passes, dumped logs, exit **1**, teardown still ran.
- **Gap found in that failure run**: the evidence file recorded no outcome. Fixed `fail()` to also write
  `checks_passed`, `failed_check` and `result=FAIL`.
- **Final verification after the fix**: full pass (**20/20, exit 0**) then a second fault injection
  (**exit 1**, evidence file ends `checks_passed=19`, `failed_check=20: …`, `result=FAIL`). Containers
  were all removed afterwards.

### Evidence from the final passing run (`TestResults/e2e-evidence.txt`)

```
# BOSS NextGen - Phase 0 end-to-end smoke test evidence
started_at_utc=2026-10-18T05:46:41Z
git_commit=3f14cf9
run_id=1792302401
correlation_id=e2e-1792302401
admin_user_id=ab73aaa7-07b3-4f68-81cd-b1b919de1c9f
widget_id=5374d1d5-f8de-4268-b858-709c9e553669
widget_event_id=bbfced13-15f3-45d1-a85a-5544ddc48417
rule_code=E2E-DEM-1792302401
rule_id=bf222284-a1f6-4a7b-93ae-13df30335676
evaluation_id=c61072d6-43f8-47f3-a62f-c9904494e5dd
config_entry_id=e1cda436-8d1b-4837-a100-94cbea058138
notification_event_id=2446cfd0-72ec-4c6a-8e03-8dc0ad1aa15c
notification_id=60c9396c-dfea-4a47-b036-8f47a538a90b
notification_email=e2e-1792302401@example.com
notification_deliveries=Email=Delivered,InApp=Delivered
checks_passed=20
result=PASS
```

(`git_commit` is the last commit at run time; the Day 40 changes were not yet committed.)

## Step 6 — First run on GitHub Actions failed; root cause was the script, not the product

After the push, the new `e2e-smoke-test` job **failed** (the other six jobs passed). The CI log showed
checks 1-16 passing and `FAIL [17] Publish log line for the notification event did not carry our
correlation ID`. Cause: that one check read `docker compose logs` once, immediately after the request,
while the equivalent widget-event check (10) *polls*; on the busier runner Docker's log delivery lagged a
moment behind the container's stdout, so the line was not yet visible. The failure dump could not confirm
this directly because `docker compose logs --tail=200` had already scrolled the Api's "Published event"
lines out. Fix (separate follow-up commit): the check now polls (up to 20 s) like the others, and the
failure dump uses `--tail=500`. Re-ran locally from a clean environment: 20/20, exit 0. **Not yet proven
on GitHub**: checks 18-20 never executed on CI because the run stopped at 17, so the next CI run is the
real confirmation. Local success had hidden a timing race that only the CI environment exposed.

## Environment notes

Host port 5432 was free, so the unmodified `docker-compose.yml` was used throughout. No RabbitMQ
first-boot failure surfaced in any of the four runs (the script retries `up` up to 3 times anyway).

## What this does and does not show

It shows the foundation pieces work together through the real edge, and that a correlation ID survives
the Gateway, the event bus, the audit store and the publish log. It does **not** show one business action
flowing through rules -> audit -> notification (that wiring does not exist), and the correlation ID is
not stored on decision-trace rows or the notification record.

## Not done / deferred

- The new CI job failed on its first GitHub run (see Step 6); the fix is verified locally only until the
  next CI run completes.
- Negative/security cases, failure isolation and observability signals are not part of this pass.
- Nothing committed or pushed (awaiting explicit instruction).
- Pre-existing note, not new to Day 40: every script in `build/scripts/` is committed as mode 100644
  (non-executable). GitHub Actions runs them as `bash <script>`, which works; the parked Azure file
  invokes them directly, so if it is ever activated all the scripts (including this one) would need
  their executable bit set. The new script follows the existing convention rather than diverging.
