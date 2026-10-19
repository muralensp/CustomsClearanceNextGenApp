# Phase 0 Sign-off Note — Platform Foundation

**Status: DECIDED, GO (declared by the Product Owner on 2026-10-19).** Prepared by Claude on Day 50 from the Day 46-49
records. The go/no-go was the Product Owner's to make; see section 7 for how it was recorded.
Evidence is cited by log/file so it can be re-checked; nothing here was re-run on Day 50 (no code changed since
Day 48; `main` at `872cc05` differs from the Day 48 tree only by documentation).

## 1. What Phase 0 set out to deliver

Plan section 2.2: architecture, CI/CD, identity, logging, monitoring, API gateway, event bus, security base
framework, and the rules-engine skeleton (storage, versioning, evaluation; no business rules). "No business
modules yet."

## 2. Result against that scope

| Plan item | Delivered | Evidence |
|---|---|---|
| CI/CD | GitHub Actions: build, lint, unit + coverage gate, integration, contract, container smoke, E2E, dependency scan, secret scan, CodeQL, `CI passed` aggregate; branch protection on `main` | ADR-0005, 0038; recent PRs passed all checks |
| Containers / local env | docker-compose stack (Gateway, Api, PostgreSQL, RabbitMQ, Mailpit, Loki, Promtail, Prometheus, Tempo, Grafana) | ADR-0015, `docs/runbooks/RUN-LOCALLY.md` |
| API gateway | YARP, sole published edge, own JWT validation, rate limiting, internal-only `/metrics` | ADR-0016, 0019, 0041 |
| Identity | in-house login, role JWTs (60-minute default) | ADR-0017, 0041 |
| Event bus | RabbitMQ, InMemory fallback, tracing across the hop, publish-channel reuse | ADR-0018, 0024, 0040 |
| Logging / monitoring | JSON logs to Loki, OTel metrics to Prometheus, traces to Tempo, Grafana alerts (no notification destination) | ADR-0020 to 0024 |
| Rules-engine skeleton | storage, versioning, evaluation pipeline, persisted decision trace, admin API, 3 India sample rules with cross-stage chaining | ADR-0025 to 0029 |
| Platform services | configuration + feature flags, notification base, durable append-only audit, placeholder reference data | ADR-0030 to 0033 |
| Security base | audit immutability at DB level, JWT key fail-fast, Production image default, SCA/secret scanning | ADR-0036, 0038, 0041 |
| Performance baseline | k6 baseline, `docs/performance/` | ADR-0037 |
| Documentation | architecture, C4, runbooks | ADR-0039 |

## 3. Exit checks (all run on 2026-10-19)

| Check | Result | Log |
|---|---|---|
| Gate 1 re-check (architecture) | Pass with noted exceptions | Day 46 |
| Gate 2 dry run (backend) | Pass with noted exceptions | Day 47 |
| Regression pass (Day 40 E2E 20/20, integration 14/14, smoke, contract; unit + coverage gate) | All green; one infra flake recorded | Day 48 |
| Build / format / secret scan / dependency scan | 0 warnings, clean, clean, 0 High/Critical | Days 46-47 |
| Phase 1 kickoff brief | Drafted, not approved | Day 49 |

Size at sign-off: 42 ADRs (40 Accepted, 2 Proposed on purpose), 215 tests, 98.7% line coverage on the measured
unit projects, 115 tracked C# files, all in Platform/Api/Gateway (plus tests). All 17 bounded-context folders are
still empty.

## 4. Known gaps and exceptions (none blocking exit)

- **From Gates 1 and 2:** rules-admin mutations are not audited; `asyncapi/` is empty; `docs/WORK-ITEMS.md` unused
  since Day 13; the cross-context `ProjectReference` check is manual; the test-name convention is followed by 73 of
  166 tests; Api endpoint logic has no measured coverage; persisted Platform types have public setters.
- **From Day 48:** the RabbitMQ first-boot race recurred twice in one contract run; `contract-test.sh` registers its
  teardown `trap` after `up -d`; the Newman collection is 1 request.
- **Security:** open items in `docs/security/PHASE0_SECURITY_CHECKLIST.md` (MFA, login lockout not built, no TLS or
  security headers, key rotation, retention, DPDPA, recipient email in an event payload). Dev credentials in compose
  and `appsettings.Development.json` are documented dev-only and guarded.
- **Never run anywhere:** `build/azure-pipelines.yml`. Reference data is unverified placeholder; tariff sourcing is
  undecided (ADR-0042 Proposed). No hosting target is chosen (PLAT-002), so nothing has been deployed outside local
  Docker and CI.

## 5. Recommendation: GO to Phase 1, with conditions

The foundation does what Phase 0 promised and its checks are repeatable. I recommend **GO**, on these conditions:

1. Before any User Management or Organization code, decide the **tenancy model** (no tenant concept exists in
   `src/`) and the **identity source**, and record both as ADRs (kickoff brief, decisions 1-2).
2. Treat the gaps in section 4 as a short first batch, not as Phase 1 scope: audit the rules-admin mutations, fix the
   `trap` ordering, restart `WORK-ITEMS.md`.
3. Do not describe the platform as deployable or production-secure: no hosting target, no TLS, no MFA, no real
   tariff data. Nothing produced so far is a basis for filing declarations.
4. Keep the single-approver risk visible: every gate here was reviewed by one person plus Claude.

A **NO-GO** would only be justified by a failing exit check or a missing Phase 0 deliverable; none was found.

## 6. Lessons learned

What worked:
- **Prove against the real system.** Live runs found what unit tests could not (the Day 40 CI timing race, the Day 41
  duplicate correlation header, the Day 45 `/metrics` exposure). The E2E script is the regression anchor.
- **Prove a test can fail.** The coverage gate was shown to fail (Day 47), not just pass.
- **Decide small, record each decision.** 42 short ADRs made the gate reviews quick to check.
- **Branch protection plus a PR per change** (Day 43 onward) gave one checked path, even for docs.
- **Parallel agents, used selectively:** about 1.5x active-time gain on Days 41/43/44 and no calendar gain, because
  the human was the bottleneck (pilot metrics report). The foundation cost (ADR-0035) is not in that figure.

What did not:
- **Self-reported durations and "done" claims were unreliable** (workers reported 2-4x the real time); verify from
  outputs and transcripts.
- **Piped exit codes** hid failures more than once despite being documented; unpiped capture is now the rule.
- **Documentation drifted** and needed a reconciliation pass (Day 44). The always-loaded `CLAUDE.md` had grown to
  ~41k tokens and was cut to ~4k (PR #32); detail now lives in logs and ADRs.
- **Environment flakiness is intermittent:** the RabbitMQ first-boot race recurred on Day 48 after the Day 34
  mitigation. `restart: on-failure` is a mitigation, not a fix; scripts should tolerate it.
- **Reading code is not testing it.** Several Day 41 security findings were established by code reading only and are
  marked as such; they need live proof when they matter.
- **Plan assumptions shifted:** Day 45 was meant as spare capacity and absorbed four real items. Expect slack to be
  consumed.

## 7. Decision

| | |
|---|---|
| Recommendation | GO to Phase 1, with the four conditions in section 5 |
| Product Owner decision | **GO** (declared by the Product Owner in the Claude Code session on 2026-10-19: "I declare it as GO") |
| Conditions | The Product Owner declared GO without amending the section 5 conditions, so they stand as the recommended entry conditions. They were not separately re-confirmed. |
| Signature | Recorded by Claude from the Product Owner's written declaration in the session, not a handwritten or digital signature. Add one here if a formal one is wanted. |

Phase 0 is **closed as GO** on that declaration. Phase 1 work still starts only on the Product Owner's explicit
instruction, after the tenancy and identity decisions in the kickoff brief.
