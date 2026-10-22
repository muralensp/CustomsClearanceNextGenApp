# CLAUDE.md

Persistent project context for Claude Code sessions on **BOSS NextGen** (Customs Clearance /
Brokerage Platform for India). This file is a short, always-loaded index. Rationale lives in `adr/`;
per-day detail (what was built, bugs found, verification evidence, files touched) lives in
`Phase-1_Architecture_Engineering_Prep/DayNN_Execution_Log.md`. The previous long-form version of this
file is preserved verbatim in `docs/PROJECT_HISTORY.md` (not auto-loaded; read it only when needed).

## Working Agreements

- Solo two-person team: the user (Product Owner/Implementer) and Claude (design/build support).
- Work proceeds one day/task at a time per the Day-Wise Implementation Plan; do not start the next
  day's work without explicit instruction.
- Nothing is "done" until proven against the real running system (real PostgreSQL via
  docker-compose, real HTTP calls, real unpiped exit codes) — not just unit tests in isolation.
- Every day's work is documented via an ADR in `adr/`, a narrative `.docx` deliverable (Phase -1 only),
  and an `Execution_Log.md`, all under `Phase-1_Architecture_Engineering_Prep/`.
- **CLAUDE.md stays small**: add at most a one-line status per day to the table below. Put detail in the
  execution log and ADR, not here.
- Commit only when explicitly requested ("Please commit it"); push only when explicitly requested.
- **`main` is protected** (`docs/BRANCH-PROTECTION.md`): every change goes through a branch + PR; required
  checks are `CI passed` and `Analyze (csharp)`; squash or rebase merge. Emergency escape hatch is in the runbook.
- Parallel multi-agent work: see `docs/PARALLEL-WORK.md`, ADR-0035 and `build/scripts/agent-env.sh <slot>`.

## Architecture Choices (one line each; ADR has the reasoning)

- **Stack**: .NET 8 / ASP.NET Core, C# 12, EF Core 8 + Npgsql (PostgreSQL primary; keep SQL Server path
  open — no provider-specific SQL/stored procs). Chosen for CargoWise parity. ADR-0001.
- **Rules engine**: configuration-driven, versioned and effective-dated (Rule / RuleVersion /
  RuleCondition / DecisionTrace), not hardcoded; compliance admins change policy without a release.
  ADR-0002, 0013, 0025-0029.
- **Repo/branching**: Monorepo + Modular Monolith + Trunk-Based Development. ADR-0003.
- **Security & audit**: high bar (ICEGATE filings, PAN/personal data, duty/GST, DPDPA 2023). ADR-0004,
  0036, 0041; open items in `docs/security/PHASE0_SECURITY_CHECKLIST.md`.
- **CI**: GitHub Actions, kept in lockstep with parked `build/azure-pipelines.yml`. ADR-0005, 0038.
- **Clean Architecture adaptation**: Domain/Application merged, bounded-context-first modules, each with
  `Context.csproj` + `Context.Infrastructure.csproj`; Platform libraries provide ports/adapters. ADR-0006.
- **Coverage gates**: 95% for core business/rules logic, 80% for CRUD-heavy services; coverlet.msbuild
  (not coverlet.collector) with `<Include>[AssemblyName]*</Include>`; SonarAnalyzer via NuGet, no Sonar
  server. ADR-0007.
- **CD**: no cloud/hosting target chosen; Docker Compose + CI smoke test only. ADR-0008, 0015.
- **Test layers**: unit (xUnit), integration (WebApplicationFactory, infrastructure-free, no DB),
  contract (Postman/Newman); only the container smoke tests touch real Postgres. ADR-0009.
- **Repository + Unit of Work**: `EfRepository` never calls `SaveChangesAsync`; the caller does, once
  per use case. ADR-0012.
- **Domain events**: `DomainEventEnvelope<TPayload>`; RabbitMQ bus (InMemory fallback when no broker
  configured). ADR-0012, 0018, 0040.
- **API host**: capabilities are `IApiModule`s in `src/Api/Modules/`, discovered by assembly scan —
  adding one means adding a file, not editing `Program.cs`. ADR-0035, `docs/runbooks/ADD-A-MODULE.md`.
- **Gateway**: YARP is the sole published edge, validates JWT itself (defense in depth); `/metrics` only
  on an internal port. ADR-0016, 0019, 0041.
- **Identity**: in-house login issuing role-based JWTs (`Jwt:AccessTokenMinutes`, default 60); no
  refresh flow or login lockout yet. ADR-0017, 0041.
- **Observability**: JSON logs → Loki, OpenTelemetry metrics → Prometheus, traces → Tempo, Grafana
  alerting (no notification destination yet). ADR-0020-0024.
- **Config / Notifications / Audit / Reference data**: `Platform.Configuration`, `Platform.Notifications`,
  `Platform.Audit.Persistence` (DB-enforced append-only via `audit_writer` role), `Platform.ReferenceData`.
  ADR-0030-0033, 0036.
- **Local dev**: Docker Compose; run steps in `docs/runbooks/RUN-LOCALLY.md`, architecture in
  `docs/Architecture/`, deployment honesty in `docs/runbooks/DEPLOY.md`. ADR-0015, 0039.

## Standing Lessons (apply proactively, don't re-derive)

- Never trust a piped command's exit code (`cmd | tail` reports `tail`'s status). Capture exit codes
  directly and unpiped. This lesson has recurred despite being documented.
- On Windows, `Environment.SetEnvironmentVariable(name, "")` **deletes** the variable. Use a non-empty
  placeholder (e.g. a single space) for "blank".
- Minimal API validates every endpoint parameter's bindability at route-**map** time. Any
  conditionally-registered service's endpoints must have their route mapping guarded by the same
  condition, not just the handler.
- `docker-compose` list-form `depends_on` waits only for container start. Use healthchecks +
  `condition: service_healthy`.
- `WebApplicationFactory` defaults its environment to "Development"; for minimal-hosting `Program.cs`,
  config is read before `ConfigureWebHost` hooks run — set overrides as process environment variables
  before the builder is created.
- A local pass can hide a timing race: poll, don't read logs once (Day 40 CI finding).
- Prove a test can fail (fault injection), not just that it passes.
- RabbitMQ first-boot race / `.erlang.cookie eacces` crashes: a plain second `up` succeeds
  (`restart: on-failure:5`, ADR-0028); persistent failures have been stray volumes or a corrupt local
  image — inspect `docker ps -a` / `docker volume ls` before changing config.
- Green CI does not prove telemetry. After OpenTelemetry bumps, check Prometheus still has `http_server_request_duration_seconds_count` (the Prometheus exporter beta must match the SDK minor).
- Worker/agent self-reported durations were 2-4x the real ones: verify from outputs, not narrative.
- Reference-data (countries, currencies, UQC units) is unverified placeholder data (`IsVerified=false`);
  never treat it as authoritative. Tariff/ITC-HS sourcing is an open decision (ADR-0042, Proposed).

## Progress (Phase -1: Days 1-20; Phase 0: Days 21-50)

One line per day; detail is in `Phase-1_Architecture_Engineering_Prep/DayNN_Execution_Log.md` and the ADR.

| Day | Deliverable | ADR |
|---|---|---|
| 1-7 | Bounded context map, ER diagrams v0.1-v1.0, context map & event catalog, API standards, OpenAPI stubs (`.docx`/`openapi/`) | — |
| 8 | Coding standards; stack decision (.NET/PostgreSQL); configurable rules engine decision | 0001, 0002 |
| 9 | Repo, branching & delivery strategy | 0003 |
| 10 | Security & audit standards (DPDPA gap closed) | 0004 |
| 11 | CI pipeline (GitHub Actions) | 0005 |
| 12 | Code scanning & coverage gates | 0007 |
| 13 | CD pipeline & containerization | 0008 |
| 14 | Test automation scaffolding (3 layers) | 0009 |
| 15 | Quality gate checklists (`docs/quality-gates/`) | — |
| 16 | Logging & error handling libraries | 0010 |
| 17 | Security & audit libraries (JWT, policies) | 0011 |
| 18 | Persistence & messaging libraries | 0012 |
| 19 | Rules-engine design spike (+ throwaway prototype in `spike/`) | 0013 |
| 20 | Phase -1 exit review — Gate 1 Pass, GO for Phase 0 | — |
| 21 | Docker Compose confirmed; bounded-context template | 0015 |
| 22 | YARP API Gateway | 0016 |
| 23 | Identity & access (login, role JWTs) | 0017 |
| 24 | RabbitMQ event bus | 0018 |
| 25 | Gateway JWT validation (defense in depth) | 0019 |
| 26 | Structured logging, Loki/Promtail/Grafana | 0020 |
| 27 | Metrics, OpenTelemetry + Prometheus + dashboard | 0021 |
| 28 | Distributed tracing, Tempo | 0022 |
| 29 | Alerting basics (no notification destination) | 0023 |
| 30 | Observability review (event-bus tracing gap fixed) | 0024 |
| 31 | Rules engine storage model | 0025 |
| 32 | Rules engine evaluation pipeline | 0026 |
| 33 | Rules engine versioning & persisted decision trace | 0027 |
| 34 | Rules engine admin API | 0028 |
| 35 | Rules engine review (3 India sample rules, cross-stage chaining proven) | 0029 |
| 36 | Configuration service + feature flags | 0030 |
| 37 | Notification base (event → in-app + SMTP) | 0031 |
| 38 | Audit trail end-to-end | 0032 |
| 39 | Seed data & reference tables (placeholder, unverified) | 0033 |
| 40 | Integration (E2E) smoke test, 20 checks, own CI job | 0034 |
| — | Parallel-work foundation: `IApiModule` refactor, slots | 0035 |
| 41 | Security hardening (audit immutability, JWT key guard, OTel upgrade) | 0036 |
| 42 | Performance baseline (k6, `docs/performance/`) | 0037 |
| 43 | CI/CD hardening; branch protection enabled | 0038 |
| 44 | Documentation skeleton (`docs/Architecture`, `docs/C4`, `docs/runbooks`) | 0039 |
| — | Multi-agent pilot metrics report (~1.5x active-time gain; use selectively) | — |
| 45 | Buffer: RabbitMQ publish-channel reuse (~2x write throughput), internal-only `/metrics` + token lifetime, Dependabot batch, India tariff-sourcing research | 0040, 0041, 0042 (Proposed) |
| 46 | Gate 1 re-check: Pass with noted exceptions (build/format/215 tests/secret scan clean; asyncapi empty, WORK-ITEMS unused) | — |
| 47 | Gate 2 dry run: Pass with noted exceptions (coverage gate proven to fail, SCA clean; rules-admin mutations unaudited) | — |
| 48 | Regression pass: all suites green (E2E 20/20, integration 14/14, contract); RabbitMQ first-boot race recurred in contract run | — |
| 49 | Phase 1 kickoff brief drafted (`docs/PHASE1-KICKOFF-BRIEF.md`); key open decision: tenancy model | — |
| 50 | Phase 0 sign-off: **GO** declared by the Product Owner 2026-10-19 (`Phase0_Signoff_Note.md`) | — |

## Status

Days 1-50 complete and merged to `main`. **Phase 0 closed as GO** (Product Owner, 2026-10-19). Phase 1 not started — wait for explicit instruction. Phase 1 decisions taken 2026-10-22 (multi-tenant, Keycloak, build order) are documented in ADR-0043 to 0049 and `docs/PHASE1-DECISIONS.md` (merged, PR #43).
Phase 0 follow-ups (`Phase0_Followups_Log.md`): script `trap` ordering and rules-admin audit done (E2E now 21 checks); `WORK-ITEMS.md` waits for Phase 1 work items.
Dependabot: #14 (Tempo 3.1.0) is superseded by the Tempo 3 migration PR (`tempo.yaml` retention moved to `overrides.defaults.compaction.block_retention`); no other Dependabot PRs open.
Known open items: `docs/security/PHASE0_SECURITY_CHECKLIST.md` (recipient address in notification event
payload, no TLS/security headers/login lockout, MFA/retention/DPDPA decisions); tariff sourcing decision
(ADR-0042); Azure DevOps YAML never run anywhere; cross-bounded-context `ProjectReference` check is
still manual (ADR-0006 follow-up) and should be automated before Phase 3 context work.
