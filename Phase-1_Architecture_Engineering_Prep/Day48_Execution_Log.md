# Day 48 Execution Log — Regression Pass

Phase 0, Week 10, Day 48. Per the Day-Wise Plan: re-run the full smoke/integration suite from Day 40 to confirm
nothing regressed during hardening (Days 41-45). Deliverable: all foundation tests green. Single verification
pass, no product code changed. Run 2026-10-19 on `main` at `34c9b8e` (branch `docs/day48-regression-pass`),
on a real docker-compose stack (Docker Desktop/WSL2). Unpiped exit codes throughout (output redirected to a file).

## Results

| Suite | Command | Result |
|---|---|---|
| Unit + coverage gate | `bash build/scripts/test.sh` (Day 47, same commit lineage) | exit 0, all thresholds met, 98.7% line |
| Integration (in-process) | `bash build/scripts/integration-test.sh` | exit 0, 14/14 passed |
| Per-feature smoke (live) | `bash build/scripts/smoke-test.sh` | exit 0 |
| Day 40 E2E smoke (live) | `bash build/scripts/e2e-smoke-test.sh` | exit 0, **20/20 checks** |
| API contract (Newman, live) | `bash build/scripts/contract-test.sh` | exit 0 on the third attempt: 1 request, 3 assertions, 0 failed (see finding) |

E2E detail (auth, gateway, sample service, rules engine, audit, notification): all 20 checks passed, including
the post-hardening behaviours: exactly one `X-Correlation-Id` response header (check 07, Day 41 fix), RabbitMQ
publish and consume with the correlation ID (10, 17, Day 45 channel reuse), audit row with old/new values (15,
Day 41 restricted `audit_writer` role), and the email received over real SMTP (20). Evidence file:
`TestResults/e2e-evidence.txt` (gitignored). Every script tears its stack down; `docker ps -a` afterwards shows
only an unrelated container from another project.

## Findings

1. **RabbitMQ first-boot race recurred, twice in a row, in the contract run** (`.erlang.cookie eacces`, the
   known issue in ADR-0028 / CLAUDE.md). `restart: on-failure` recovered the container each time (restart count 1,
   healthy within ~20 s), but `docker compose up -d` failed fast with "dependency rabbitmq failed to start:
   container is unhealthy" because the api's `depends_on: service_healthy` gave up during the restart window.
   Other suites on the same machine started cleanly (E2E, smoke), so it is intermittent, not a regression from
   Days 41-45. Third attempt (after waiting for healthy) passed.
2. **`contract-test.sh` leaves the stack running when `up` fails**: `trap cleanup EXIT` is registered after
   `docker compose up -d`, and `set -e` exits first, so no teardown runs. I ran `docker compose down -v` by hand.
   Same pattern should be checked in the other scripts. Not fixed today (no code change in a regression pass).
3. **The contract suite is thin**: 1 request, 3 assertions. It passes, but it says little about the Phase 0
   endpoints (rules, config, audit, notifications), which are covered by the E2E instead.

## Outcome

**Result: all foundation tests green.** No regression found from Days 41-45. One flaky-infrastructure
recurrence and two script weaknesses recorded, none fixed here.

Carried forward:
1. RabbitMQ race: if CI starts hitting it, give `up -d` a retry or `--wait` with a longer timeout, or add a
   RabbitMQ `start_period`. Decide when it shows up in CI, not before.
2. Move the teardown `trap` before `docker compose up` in the build scripts (small follow-up PR).
3. Broaden the Newman collection when Phase 1 endpoints exist.
4. Day 46/47 items unchanged.

Day 49 (Phase 1 kickoff prep) has not been started.
