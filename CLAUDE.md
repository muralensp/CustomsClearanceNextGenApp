# CLAUDE.md

Persistent project context for Claude Code sessions on **BOSS NextGen** (Customs Clearance /
Brokerage Platform for India). Full rationale for each decision lives in `adr/`; this file is a
durable index of what's been decided and built, kept in sync as work progresses.

## Working Agreements

- Solo two-person team: the user (Product Owner/Implementer) and Claude (design/build support).
- Work proceeds one day/task at a time per the Day-Wise Implementation Plan; do not start the next
  day's work without explicit instruction.
- Nothing is "done" until proven against the real running system (real PostgreSQL via
  docker-compose, real HTTP calls, real unpiped exit codes) — not just unit tests in isolation.
- Every day's work is documented via an ADR in `adr/`, a narrative `.docx` deliverable, and an
  `Execution_Log.md`, all under `Phase-1_Architecture_Engineering_Prep/`.
- Commit only when explicitly requested ("Please commit it"); push only when explicitly requested.

## Architecture Choices (cumulative)

- **Stack**: .NET 8 / ASP.NET Core, C# 12, EF Core 8.0.10 + Npgsql 8.0.10 (PostgreSQL primary,
  SQL Server migration path kept open — avoid provider-specific SQL/stored procs), chosen for
  parity with CargoWise (market-leading customs brokerage platform, .NET/C#) over SAP GTS/Oracle
  GTM (Java-based). See ADR-0001.
- **Rules engine**: configuration-driven, not code-driven — business rules (e.g. India de minimis
  thresholds, which vary by import channel/purpose/importer type/commodity with no single
  universal value) live in a versioned, effective-dated rule repository (Rule / RuleVersion /
  RuleCondition / DecisionTrace), not hardcoded logic, so compliance admins can update policy
  without a release. See ADR-0002; implementation scoped for Phase 0 Days 31-35, design spike at
  Day 19.
- **Repo/branching**: Monorepo + Modular Monolith + Trunk-Based Development, adapted from an
  external analysis (`Repo_BranchingStrategy_NSPMV1.docx`) to fit the solo-developer, PR-to-self
  discipline already locked in by ADR-0001/0002. See ADR-0003.
- **Security & audit**: high bar given ICEGATE government filings, personal data (e.g. gift
  imports with only PAN/no GSTIN), financial data (duty/GST/Cess), and India's DPDPA 2023
  (identified as a gap not named in the PRD and closed here). See ADR-0004.
- **CI platform**: GitHub Actions now, built to stay migration-ready to Azure DevOps later
  (`build/azure-pipelines.yml` kept parked in lockstep with `.github/workflows/ci.yml`). See
  ADR-0005.
- **Clean Architecture adaptation**: Domain/Application merged (not textbook-separate), bounded-
  context-first modules, each with its own `Context.csproj`/`Context.Infrastructure.csproj`;
  shared Platform libraries provide ports/adapters that bounded contexts build narrower interfaces
  on top of (e.g. `IDeclarationRepository` over `IRepository<TEntity, TId>`). A deliberate
  adaptation, not the textbook version — documented explicitly rather than left ambiguous. See
  ADR-0006.
- **Static analysis & coverage gates**: 95% threshold for core business/rules logic, 80% for
  CRUD-heavy master-data services (a v2 correction of an earlier blanket 95% rule). No
  SonarCloud/SonarQube server currently provisioned (named gap, not oversight) — SonarAnalyzer.CSharp
  runs locally/in-CI via NuGet analyzers instead. coverlet.msbuild (not coverlet.collector — they
  conflict) scoped with `<Include>[AssemblyName]*</Include>` to prevent transitive-dependency
  coverage bleed. See ADR-0007.
- **CD/containerization**: no cloud provider/hosting target chosen yet (deferred to Phase 0 Day 21
  infra buildout, deliberately). Day 13 scope is containerize the Api host + prove it via a
  docker-compose-based CI smoke test only — not a persistently hosted environment. See ADR-0008.
- **Test layering**: three isolated layers — unit (xUnit + coverlet), integration
  (WebApplicationFactory-based, must stay infrastructure-free, no real database), and API contract
  (Postman/Newman) — kept isolated so a slow/environment-dependent test in one layer never blocks
  or dilutes another layer's fast feedback loop. Only the container smoke-test layer touches real
  Postgres. See ADR-0009.
- **Repository + Unit of Work**: `EfRepository<TEntity, TId>` deliberately does not call
  `SaveChangesAsync` itself — the Unit of Work stays with the caller (Application layer calls
  `SaveChangesAsync` once per use case). See ADR-0012.
- **Domain events**: unified envelope (`DomainEventEnvelope<TPayload>`: EventId, EventType,
  EventVersion, OccurredAt, CorrelationId, Source, Payload) per Day 6 API Standards v1.1.
  `InMemoryEventBus` is a same-process placeholder; a real broker (Kafka/RabbitMQ/Azure Service
  Bus) is a Phase 0 decision, swapped in behind `AddCustomsClearanceMessaging()` without changing
  the envelope/handler contract. See ADR-0012.
- **Local dev environment**: Docker Compose (not local Kubernetes) — matches solo-developer scale;
  cloud/hosting target for Staging/Production remains deliberately deferred (no real deployment
  need yet). "Base service template" reinterpreted for the Modular Monolith (ADR-0003) as a
  bounded-context project template (`templates/BoundedContextTemplate/`: `Context.csproj` +
  `Context.Infrastructure.csproj`, per ADR-0006's split), not a new per-context container. See
  ADR-0015.

## Standing Lessons (apply proactively, don't re-derive)

- Never trust a piped command's exit code (`cmd | tail` reports `tail`'s exit status, not `cmd`'s).
  Always capture exit codes directly and unpiped, especially for scripts with explicit `exit 1`
  paths.
- On Windows, `Environment.SetEnvironmentVariable(name, "")` **deletes** the variable rather than
  setting it empty. Use a non-empty placeholder (e.g. a single space) to represent "blank."
- Minimal API's `RequestDelegateFactory` validates every endpoint parameter's bindability at
  route-**map** time, not per-request. An unregistered service parameter on one endpoint crashes
  the entire app at startup, including unrelated routes. Any conditionally-registered service's
  endpoints must have their route mapping — not just their handler logic — guarded by the same
  condition.
- `docker-compose`'s list-form `depends_on` only waits for container **start**, not readiness. Use
  healthchecks (e.g. `pg_isready`) + `depends_on: condition: service_healthy` for services with a
  real startup sequence.
- `WebApplicationFactory` defaults its hosting environment to "Development" regardless of explicit
  `UseEnvironment` calls in test code that runs after config is already read.
- For minimal-hosting (top-level statements) `Program.cs`, config reads happen synchronously while
  building `WebApplicationBuilder`, before `WebApplicationFactory.ConfigureWebHost` hooks apply —
  overrides must be set as process environment variables before the builder is created, not via
  `ConfigureAppConfiguration`.

## Day-by-Day Deliverables Log (Phase -1: Architecture & Engineering Preparation)

| Day | Deliverable | Key artifacts | ADR |
|---|---|---|---|
| 1 | Bounded Context Map (incl. India de minimis domain constraint) | `Day1_Bounded_Context_Map*.docx` | — |
| 2 | ER Diagram v0.1 | `Day2_ER_Diagram_v0.1.docx` | — |
| 3 | ER Diagram v0.2 (Rule/RuleVersion/RuleCondition/DecisionTrace model) | `Day3_ER_Diagram_v0.2.docx` | — |
| 4 | ER Diagram v1.0 + scenario revalidation | `Day4_ER_Diagram_v1.0.docx`, `Day4_Scenarios_Revalidation_against_v1.1.docx` | — |
| 5 | Context Map & Domain Event Catalog v1.0/v1.1 | `Day5_Context_Map_and_Domain_Event_Catalog_v1.*.docx` | — |
| 6 | API Standards v1.0/v1.1 (incl. domain event envelope shape) | `Day6_API_Standards_v1.*.docx` | — |
| 7 | OpenAPI Stubs Transmittal | `Day7_OpenAPI_Stubs_Transmittal.docx`, `openapi/` | — |
| 8 | Coding Standards v1.0; Technology Stack decision (.NET/PostgreSQL) | `Day8_Coding_Standards_v1.0.docx` | ADR-0001 |
| 8 (extended) | Configurable, effective-dated Rules Engine decision | — | ADR-0002 |
| 9 | Repository, Branching & Delivery Strategy (Monorepo + Modular Monolith + Trunk-Based Dev) | `Day9_Repository_Branching_Strategy.docx` | ADR-0003 |
| 10 | Security & Audit Standards (incl. DPDPA 2023 gap closed) | `Day10_Security_Audit_Standards_v1.0.docx` | ADR-0004 |
| 11 | CI Pipeline Setup (GitHub Actions, Azure DevOps-migration-ready) | `Day11_CI_Pipeline_Setup.docx` | ADR-0005 |
| 12 | Code Scanning & Coverage Gates (95%/80% thresholds) | `Day12_Code_Scanning_Coverage_Gates.docx`, `Day12_Execution_Log.md` | ADR-0007 |
| 13 | CD Pipeline & Environments (containerize + docker-compose smoke test) | `Day13_CD_Pipeline_and_Environments.docx`, `Day13_Execution_Log.md` | ADR-0008 |
| 14 | Test Automation Scaffolding (3 isolated layers: unit/integration/contract) | `Day14_Test_Automation_Scaffolding.docx`, `Day14_Execution_Log.md` | ADR-0009 |
| 15 | Quality Gate Checklists (Architecture/Backend/API/Frontend/Release gates made concrete) | `Day15_Quality_Gate_Checklists.docx`, `Day15_Execution_Log.md` | — |
| 16 | Logging & Error Handling Platform libraries | `src/Platform/Logging/`, `src/Platform/ErrorHandling/`, `Day16_Execution_Log.md` | ADR-0010 |
| 17 | Security & Audit Platform libraries (JWT auth, role policies, audit trail) | `src/Platform/Security/`, `src/Platform/Audit/`, `Day17_Execution_Log.md` | ADR-0011 |
| 18 | Persistence & Messaging Platform libraries (EF Core repo base, in-process pub/sub) | see below | ADR-0012 |
| 19 | Rules-Engine Design Spike — design doc complete; throwaway prototype pending separate confirmation | `Day19_Execution_Log.md` | ADR-0013 |
| 20 | Phase -1 Exit Review — Gate 1 walked retrospectively, Pass, go decision for Phase 0 | `Day20_Phase-1_Exit_Review.docx`, `Day20_Execution_Log.md` | — |

Days 1-7 predate formal ADR numbering; their decisions are captured directly in the referenced
`.docx` deliverables and carried forward into ADR-0001/0002 once formalized on Day 8.

### Day 18 — Persistence & Messaging Platform Libraries

**Decision**: Built `CustomsClearance.Platform.Persistence` (`AuditableEntity<TId>`,
`IRepository<TEntity, TId>`, `EfRepository<TEntity, TId>`, `PlatformDbContext` — auto-stamps
`CreatedAt`/`UpdatedAt` in `SaveChangesAsync`) and `CustomsClearance.Platform.Messaging`
(`DomainEventEnvelope<TPayload>`, `IEventPublisher`, `IEventHandler<TPayload>`,
`InMemoryEventBus`), both proven against the real Api host via Development-only
`DiagnosticsDbContext`/`/diagnostics/widgets` endpoints (real PostgreSQL write + real event
publish/handle round trip).

**Five real bugs found and fixed** (full detail: `adr/0012-persistence-and-messaging-libraries.md`):
1. `InMemoryEventBus` couldn't resolve Scoped handlers from a Singleton's root provider — fixed by
   injecting `IServiceScopeFactory` and creating a scope per `PublishAsync` call.
2. Postgres startup race crashed the whole Api process — fixed with a `pg_isready` healthcheck +
   `depends_on: condition: service_healthy` in `docker-compose.yml`, plus a 5-attempt retry/backoff
   around `EnsureCreatedAsync()` in `Program.cs` as defense in depth.
3. A "successful" smoke-test run was actually a masked failure (piped through `tail`) — fixed by
   always capturing exit codes directly, unpiped.
4. Integration-test DB-skip override silently failed twice (`ConfigureAppConfiguration` too late
   for minimal-hosting `Program.cs`; then `SetEnvironmentVariable(name, "")` deletes the var on
   Windows) — fixed with a single-space placeholder set in `TestApiFactory`'s static constructor.
5. Minimal API route mapping crashed the entire app at startup on an unresolvable `DbContext`
   parameter — fixed by moving the widget endpoints' `MapPost`/`MapGet` inside the same
   connection-string guard controlling `DiagnosticsDbContext` registration.

**Files added/modified**:
- `src/Platform/Persistence/` — new (`.csproj`, `AuditableEntity.cs`, `IRepository.cs`,
  `PlatformDbContext.cs`, `EfRepository.cs`)
- `src/Platform/Messaging/` — new (`.csproj`, `DomainEventEnvelope.cs`, `IEventPublisher.cs`,
  `IEventHandler.cs`, `InMemoryEventBus.cs`, `ServiceCollectionExtensions.cs`)
- `tests/Unit/Platform.Persistence.Tests/`, `tests/Unit/Platform.Messaging.Tests/` — new
- `src/Api/Diagnostics/` — new (`DiagnosticsDbContext.cs`, `DiagnosticsWidgetCreatedHandler.cs`)
- `src/Api/Program.cs` — persistence/messaging wired in, retry logic, endpoint-registration guard
- `src/Api/appsettings.Development.json` — `ConnectionStrings:DiagnosticsDb` added
- `src/Api/Dockerfile` — 2 new Platform projects copied into build context
- `docker-compose.yml` — postgres healthcheck, `api` `depends_on` condition, connection string env
  override
- `build/scripts/smoke-test.sh` — real Postgres round-trip check (POST + GET `/diagnostics/widgets`)
- `tests/Integration/Api.IntegrationTests/TestApiFactory.cs` — new, shared factory + env override
- `tests/Integration/Api.IntegrationTests/{Health,ErrorHandling,Secure}EndpointTests.cs` — updated
  to use `TestApiFactory`
- `CustomsClearance.sln` — 4 new projects added
- `adr/0012-persistence-and-messaging-libraries.md`,
  `Phase-1_Architecture_Engineering_Prep/Day18_Persistence_and_Messaging_Libraries.docx`,
  `Day18_Execution_Log.md`, `Day18_Session_Summary.md` — new

**Verification**: build (exit 0, 0 warnings at the time; since Day 28 the build shows the known NU1902 warnings), lint (exit 0), unit tests (exit 0, 7 projects at the time; 12 by Day 40, 96.5%
aggregate coverage), integration tests (exit 0, 6/6, 59ms, zero infrastructure dependency),
container smoke test (exit 0, real Postgres round-trip, independently confirmed via direct `psql`
query). Committed and pushed as `bcc353c`; all 6 GitHub Actions CI jobs confirmed green.

**Status**: Complete. Next unstarted item is **Day 19 — Rules-Engine Design Spike**, pending
explicit instruction to begin.

### Day 19 — Rules-Engine Design Spike (design doc phase)

**Decision**: Designed (not implemented) the rules-engine evaluation pipeline as five composable
interfaces — `IFactsProvider`, `IRuleSetLoader` (effective-dated, per-stage), `IConditionEvaluator`
(AND-only, per Day 3/ADR-0002), `IPriorityResolver` (lowest-`Priority`-wins, deterministic RuleId
tiebreak per PRD Ch.5.14), `IDecisionTraceRecorder` (one trace row per PRD Ch.5.7 pipeline stage,
including "no match" steps) — orchestrated by `RulesEngine.EvaluateAsync` against PRD Ch.5.7's
fixed sequential stage order (Country → Customer → Shipment → Commodity → HS Code → Duty → Permit
→ Shipment Indicators → Warning → Final CT Assignment → Status Update). Key call: `Facts` is
mutated in place across stages (not frozen at pipeline start), because Ch.5.7's sequential order
means later stages (e.g. Permit Rules) depend on earlier stages' outcomes (e.g. HS Code Rules
setting `ControlledGoodsFlag`). Full detail and alternatives considered:
`adr/0013-rules-engine-design-spike.md`.

**Explicitly deferred to Phase 0 Days 31-35**: Admin CRUD API for Rule/RuleVersion/RuleCondition
authoring, Re-Segregation event wiring (PRD Ch.5.11), Manual Override write path (PRD Ch.5.12),
and the EF Core implementation of `IRuleSetLoader`.

**Prototype validation (Day 19 continued)**: Built a throwaway spike at `spike/RulesEngineSpike/`
(in-memory only, not added to `CustomsClearance.sln`) implementing all five ADR-0013 interfaces
against 3 PRD Ch.5.10 decision-table scenarios (FTZ, HS Code Controlled, De Minimis). All 3 pass
(`dotnet run`, exit 0, unpiped). Found and fixed 2 real design bugs (full detail in
`adr/0013-rules-engine-design-spike.md`'s Consequences section): (1) the "clearance type assigned"
flag must be set explicitly in `ApplyOutcome`, or PRD's default "Otherwise → CT=INF" rule re-fires
and clobbers every earlier stage's outcome; (2) `RuleCategory` values must be unique per PRD Ch.5.7
pipeline stage — reusing one category across two stages (FTZ and De Minimis both under `Shipment`)
caused the loader to load both rules into both stages.

**Status**: Design doc + prototype validation complete.

### Day 20 — Phase -1 Exit Review

**Decision**: Walked `docs/quality-gates/Gate1_Architecture_Review.md` retrospectively against all
Phase -1 work (Days 1-19), since Gate 1 is explicitly scoped to run "at the start of implementing
any new bounded context module, and at Phase -1 exit (Day 20)." All five sections (Standards
Followed, Naming Conventions, Security, Performance, Documentation) passed — most Standards/
Security/Performance checks are N/A at this stage since all 17 bounded contexts remain empty
scaffolding with zero cross-context `ProjectReference`s. One documentation gap found and fixed:
`adr/0013-rules-engine-design-spike.md` was still `Status: Proposed` despite Phase 0 Days 31-35
being scoped to build directly against it — formally accepted as part of this review.
`adr/0014-ai-ml-integration-strategy.md` (added same-day, in response to an ad hoc question about
integrating Digicust-style AI features) was deliberately left `Proposed` — it records strategy for
a later phase, not a settled decision the codebase is built against yet. Full-solution
verification re-run as part of the review: build (0 warnings/errors), `dotnet format
--verify-no-changes` (clean), and `dotnet test` (9/9 passed). **Result: Pass — GO decision** to
begin Phase 0.

**Noted, non-blocking exception carried forward**: the cross-bounded-context `ProjectReference`
check remains manual (an ADR-0006-tracked follow-up, not yet automated) — confirmed clean by
direct inspection for Day 20, but should be automated before Phase 0 bounded-context
implementation begins in earnest.

**Files added**: `Phase-1_Architecture_Engineering_Prep/Day20_Phase-1_Exit_Review.docx`,
`Phase-1_Architecture_Engineering_Prep/Day20_Execution_Log.md`. `adr/0014-ai-ml-integration-strategy.md`
added (Proposed) and `adr/0013-rules-engine-design-spike.md` updated (Proposed → Accepted).

**Status**: Complete. Phase -1 (Days 1-20) is closed. Next unstarted item is **Phase 0, Day 21 —
infrastructure/CD/hosting decision** (per ADR-0008's deliberate deferral), pending explicit
instruction to begin.

## Day-by-Day Deliverables Log (Phase 0: Foundation & Core Services)

| Day | Deliverable | Key artifacts | ADR |
|---|---|---|---|
| 21 | Environment & Containerization — Docker Compose confirmed as local dev env, "hello world" (`/health`) re-proven end-to-end against real Postgres, bounded-context project template added | `adr/0015-*.md`, `templates/BoundedContextTemplate/`, `Day21_Execution_Log.md` | ADR-0015 |
| 22 | API Gateway — YARP gateway (`src/Gateway`) as sole published edge, routes to `Api` host's `/health`, rate limiting + auth passthrough proven live | `adr/0016-*.md`, `src/Gateway/`, `Day22_Execution_Log.md` | ADR-0016 |
| 23 | Identity & Access — in-house login (`POST /auth/login`) issuing role-based JWTs, Postgres-backed dev-persona users, proven end-to-end incl. wrong-role 403 | `adr/0017-*.md`, `src/Api/Identity/`, `src/Platform/Security/ITokenIssuer.cs`, `Day23_Execution_Log.md` | ADR-0017 |
| 24 | Event Bus — real RabbitMQ swap for `InMemoryEventBus` (`RabbitMqEventBus`/`RabbitMqEventConsumer`), sample event flow proven end-to-end incl. CI smoke-test | `adr/0018-*.md`, `src/Platform/Messaging/RabbitMq*.cs`, `Day24_Execution_Log.md` | ADR-0018 |
| 25 | Base Security Framework — gateway now performs its own JWT validation (defense-in-depth) via YARP per-route `AuthorizationPolicy`, proven independent of the `Api` host's own enforcement | `adr/0019-*.md`, `src/Gateway/appsettings.json`, `Day25_Execution_Log.md` | ADR-0019 |
| 26 | Structured Logging Rollout — JSON console logging (`Api`/`Gateway`) + Loki/Promtail/Grafana centralized log view, proven queryable by CorrelationId across the full publish→consume lifecycle | `adr/0020-*.md`, `observability/`, `src/Platform/Logging/LoggingBuilderExtensions.cs`, `Day26_Execution_Log.md` | ADR-0020 |
| 27 | Monitoring & Metrics — OpenTelemetry + Prometheus (`Platform.Observability`), provisioned Grafana dashboard (health/latency/error-rate), queries verified against real metric names, not guessed | `adr/0021-*.md`, `src/Platform/Observability/`, `observability/dashboards/`, `Day27_Execution_Log.md` | ADR-0021 |
| 28 | Distributed Tracing — OpenTelemetry tracing + Grafana Tempo, spans tagged with the existing CorrelationId, proven as one real multi-span trace spanning the Gateway→Api hop, queryable via Grafana's own datasource proxy | `adr/0022-*.md`, `src/Platform/Observability/`, `observability/tempo.yaml`, `Day28_Execution_Log.md` | ADR-0022 |
| 29 | Alerting Basics — Grafana unified alerting (Service Down, High 5xx Error Rate), proven via a real forced failure/recovery state transition, notification delivery deliberately deferred (no real destination yet) | `adr/0023-*.md`, `observability/alerting/rules.yaml`, `Day29_Execution_Log.md` | ADR-0023 |
| 30 | Observability Review — found and fixed a real gap (tracing never covered the RabbitMQ event bus, against Day 28's own stated scope); proved logs/metrics/traces/alerts all correlate for one real request that also produced one real event | `adr/0024-*.md`, `src/Platform/Messaging/MessagingTelemetry.cs`, `Day30_Execution_Log.md` | ADR-0024 |

## Day-by-Day Deliverables Log (Phase 0, Week 7: Rules Engine Skeleton — Days 31-35, non-parallelizable)

| Day | Deliverable | Key artifacts | ADR |
|---|---|---|---|
| 31 | Rules Engine: Storage Model — Rule/RuleVersion/RuleCondition schema migrated against real PostgreSQL, two real bugs found and fixed (wrong DbContext resolved via DI; JSON serialization cycle) | `adr/0025-*.md`, `src/Platform/RulesEngine/`, `Day31_Execution_Log.md` | ADR-0025 |
| 32 | Rules Engine: Evaluation Pipeline — EfRuleSetLoader/ConditionEvaluator/PriorityResolver/RulesEngine orchestration, 45 unit tests (100%/97.53% coverage), proven live against a real Postgres-loaded rule via a new diagnostic endpoint | `adr/0026-*.md`, `src/Platform/RulesEngine/RulesEngine.cs`, `Day32_Execution_Log.md` | ADR-0026 |
| 33 | Rules Engine: Versioning & Decision Trace — real persisted DecisionTrace entity replacing Day 32's in-memory placeholder, proven queryable via a new diagnostic endpoint; corrupted local RabbitMQ image root-caused (different from prior days' stray-volume findings) | `adr/0027-*.md`, `src/Platform/RulesEngine/DecisionTrace.cs`, `Day33_Execution_Log.md` | ADR-0027 |
| 34 | Rules Engine: Admin API (minimal) — real RequireAdmin-gated CRUD API for rules; two real bugs fixed (enum JSON binding, malformed-request 500→400); RabbitMQ startup race finally root-caused and fixed with a self-healing restart policy + CI retry, not another one-off workaround | `adr/0028-*.md`, `src/Api/RulesAdmin/`, `Day34_Execution_Log.md` | ADR-0028 |
| 35 | Rules Engine Review — 3 real India-specific sample rules (De Minimis gift imports, HS Code 9018 controlled goods, Permit) loaded through the real Day 34 admin API, evaluated via a new multi-fact diagnostic endpoint; first live proof of ADR-0013's mutable-Facts cross-stage chaining | `adr/0029-*.md`, `Day35_Execution_Log.md` | ADR-0029 |

## Day-by-Day Deliverables Log (Phase 0, Week 8: Configuration & Notification Base — Days 36-40)

| Day | Deliverable | Key artifacts | ADR |
|---|---|---|---|
| 36 | Configuration Service — `Platform.Configuration` (typed key/value + feature flags in Postgres, 30s cache invalidated on write), Admin-gated `/api/config/*` CRUD; a value changed live with no restart | `adr/0030-*.md`, `src/Platform/Configuration/`, `src/Api/ConfigAdmin/`, `Day36_Execution_Log.md` | ADR-0030 |
| 37 | Notification Base — `Platform.Notifications`: `NotificationRequested` event over RabbitMQ → persisted in-app notification + SMTP email (Mailpit in dev), per-channel delivery records, idempotent on redelivery | `adr/0031-*.md`, `src/Platform/Notifications/`, `src/Api/Notifications/`, `Day37_Execution_Log.md` | ADR-0031 |
| 38 | Audit Trail End-to-End — `Platform.Audit.Persistence` (append-only PostgreSQL audit store replacing the console writer), config create/update/delete audited with old/new value + user + timestamp, Compliance/Admin `GET /api/audit` | `adr/0032-*.md`, `src/Platform/AuditPersistence/`, `src/Api/AuditTrail/`, `Day38_Execution_Log.md` | ADR-0032 |
| 39 | Seed Data & Reference Tables — `Platform.ReferenceData`: placeholder countries (49), currencies (INR + 12), India UQC units (45) seeded idempotently from embedded JSON; every row `Source=Placeholder-Day39`, `IsVerified=false`; read-only `/api/reference/*` | `adr/0033-*.md`, `src/Platform/ReferenceData/`, `src/Api/ReferenceData/`, `Day39_Execution_Log.md` | ADR-0033 |
| 40 | Integration Smoke Test — `build/scripts/e2e-smoke-test.sh` + own CI job/stage: one sequential 20-check pass over auth → gateway → sample service → rules engine → audit → notification, evidence file, stops at first failure; passed 20/20 and proven able to fail | `adr/0034-*.md`, `build/scripts/e2e-smoke-test.sh`, `Day40_Execution_Log.md` | ADR-0034 |

### Day 21 — Environment & Containerization

**Decision**: Confirmed **Docker Compose** (not local Kubernetes) as the local/dev environment —
unchanged from Day 13/18's `docker-compose.yml`, just formally decided rather than left implicit.
Cloud/hosting target for Staging/Production stays deliberately deferred (ADR-0008's rows remain
"Not yet implemented"). Reinterpreted the Day-Wise Plan's "base service template project" for this
project's **Modular Monolith** (ADR-0003): rather than a new per-context container/microservice
scaffold, built a **bounded-context project template** (`templates/BoundedContextTemplate/`) — a
`Context.csproj` + `Context.Infrastructure.csproj` pair matching ADR-0006's split — for Phase 3+
use, when a bounded context's real build actually starts. Both decisions and the reasoning are
recorded in ADR-0015, following the same "adapt and document explicitly" pattern ADR-0006 set.

**Verification**: the "hello world in a container" deliverable was re-proven end-to-end today
(not assumed from Day 13/18's history) — Docker Desktop started, `docker compose up -d --build`
(fresh image), `GET /health` → 200, `POST /diagnostics/widgets?name=day21-check` → 200 with
container logs confirming a real `CREATE TABLE "Widgets"` DDL statement against the `postgres`
service, `docker compose down` (containers/network removed, named volume left intact for dev
persistence).

**Files added**: `adr/0015-local-dev-environment-and-context-template.md`,
`templates/BoundedContextTemplate/{Context.csproj.template, Context.Infrastructure.csproj.template,
README.md}`, `Phase-1_Architecture_Engineering_Prep/Day21_Execution_Log.md`. No changes to
`docker-compose.yml`/`src/Api/Dockerfile` — Day 21 confirmed the existing setup rather than
replacing it.

**Status**: Complete. Next unstarted item is **Day 22 — API Gateway**, pending explicit
instruction to begin. Per ADR-0015's Consequences, Days 22-25 will likely need the same kind of
Modular-Monolith reinterpretation Day 21 needed (middleware/library concerns wired into the single
`Api` host rather than standalone services) — flagged now, not decided ahead of that day's work.

### Day 22 — API Gateway

**Decision**: Built a real YARP-based gateway (`src/Gateway/CustomsClearance.Gateway.csproj`) as
the sole published edge, per Day6_API_Standards_v1.1.docx Section 2 ("gateway for external
ingress only; internal calls authenticated") — this turned out to be an existing standard being
implemented for the first time, not a new architectural surface, and fits the Modular Monolith
(ADR-0003) cleanly since it fronts the one `Api` host, not several services. Technology choice
(YARP over Ocelot/nginx) confirmed with the user before building. `docker-compose.yml`: `gateway`
now publishes host port 8080 (where `api` used to); `api` is internal-only
(`http://api:8080` on the compose network). Single catch-all route to the `Api` host; global
fixed-window rate limiter (100 req/10s/IP, 429 on rejection — a dev-time default, not tuned);
`Authorization` header passthrough (no auth performed at the gateway itself); own `/__gateway/health`
liveness endpoint distinct from the proxied `/health`.

**Real bug found and fixed**: `Platform.Logging`'s `CorrelationIdMiddleware` (Day 16) only wrote
the correlation ID onto the response, never the request — invisible in a single-host setup, but
meant a gateway-generated ID never reached the proxied `Api` destination via YARP (which forwards
request headers downstream by default), so `Api` minted its own separate ID. Fixed by also setting
`context.Request.Headers[HeaderName]`; re-verified end-to-end (single consistent ID, both
gateway-generated and client-supplied cases) and `Platform.Logging.Tests` still 100% coverage.

**Verification**: full container round trip (not assumed from Day 21) — `docker compose up
--build`, gateway's own health + proxied `Api` health both 200, direct `Api` port access
confirmed closed, a real Postgres-backed `POST /diagnostics/widgets` proxied successfully, rate
limiting confirmed via a concurrent 150-request burst (121×200/29×429) with window-reset
re-verified, and `build/scripts/smoke-test.sh` (the actual CI script) passed unmodified since it
already targets `localhost:8080`. Full-solution build/format/test re-run clean.

**Files added**: `src/Gateway/` (csproj, `Program.cs`, `appsettings.json`, `Dockerfile`),
`adr/0016-api-gateway.md`, `Phase-1_Architecture_Engineering_Prep/Day22_Execution_Log.md`.
**Files modified**: `docker-compose.yml` (gateway service added, `api`'s host port removed),
`CustomsClearance.sln` (Gateway project added), `src/Platform/Logging/CorrelationIdMiddleware.cs`
(request-header propagation fix).

**Status**: Complete. Next unstarted item is **Day 23 — Identity & Access**, pending explicit
instruction to begin.

### Day 23 — Identity & Access

**Decision**: Built in-house minimal Identity (not a managed provider — consistent with
`JwtOptions`' own Day 17 phasing language and Day 21's deferred cloud commitment) as a capability
**inside** the `Api` host (`src/Api/Identity/`), not a new deployable — unlike Day 22's Gateway,
nothing in Day6 API Standards/ADR-0004 requires login to be network-separate. `Platform.Security`
gains `ITokenIssuer`/`JwtTokenIssuer` (issuance, registered via
`AddCustomsClearanceTokenIssuance`, separate from the existing `AddCustomsClearanceAuthentication`
which only validates). A real Postgres-backed `IdentityDbContext`/`PlatformUser` table (same
placeholder status as `DiagnosticsDbContext`) seeds one dev user per PRD persona
(broker/supervisor/compliance/admin), password-hashed via `PasswordHasher<TUser>`. `POST
/auth/login` issues a role-based JWT on success.

**Three real bugs found and fixed**: (1) `TestApiFactory` needed `ConnectionStrings__IdentityDb`
blanked too, alongside the existing `DiagnosticsDb` override — a second instance of Day 18's
already-documented `WebApplicationFactory` connection-string-timing lesson. (2)
`IdentityDbContext.EnsureCreatedAsync()` silently created no tables (`relation "Users" does not
exist` crash), because `EnsureCreatedAsync` only creates the *database* if missing, and Diagnostics
had already created it — fixed with a fallback to `CreateTablesAsync()` when the database already
existed, tolerating "already exists" for idempotent restarts, verified against both a warm restart
and a full `docker compose down -v` fresh-volume cycle. (3) Adding `JwtTokenIssuer` without direct
tests tripped ADR-0007's 80% coverage gate on `Platform.Security` — fixed with
`JwtTokenIssuerTests`/`ServiceCollectionExtensionsTests`, extracting a shared `TestAuthHost` test
helper rather than duplicating the existing test's host-building code.

**Meta-finding**: the first two verification passes in this session were piped through `tail`,
silently reporting exit 0 while the real command underneath had failed (2 `dotnet format` IDE1006
violations, then the coverage-gate failure) — the exact "never trust a piped exit code" standing
lesson already in this file, recurring despite being documented. Re-running unpiped is what
surfaced both.

**Verification**: full role-based login flow proven live against real containers — correct
login → JWT → 200 on an Admin-only endpoint; broker's JWT against the same endpoint → 403; wrong
password/unknown user → 401; no token → 401. Full-solution build/format/test and the actual CI
`smoke-test.sh` all re-confirmed clean with unpiped exit codes.

**Files added**: `src/Platform/Security/ITokenIssuer.cs`, `JwtTokenIssuer.cs`; `src/Api/Identity/`
(`IdentityDbContext.cs`, `IdentitySeeder.cs`, `LoginModels.cs`); `tests/Unit/Platform.Security.Tests/`
(`JwtTokenIssuerTests.cs`, `ServiceCollectionExtensionsTests.cs`, `TestAuthHost.cs`);
`adr/0017-identity-and-access.md`; `Phase-1_Architecture_Engineering_Prep/Day23_Execution_Log.md`.
**Files modified**: `src/Api/Program.cs`, `src/Api/CustomsClearance.Api.csproj` (added
`Microsoft.Extensions.Identity.Core`), `src/Api/appsettings.Development.json`,
`docker-compose.yml`, `src/Platform/Security/ServiceCollectionExtensions.cs`,
`tests/Integration/Api.IntegrationTests/TestApiFactory.cs`,
`tests/Unit/Platform.Security.Tests/AuthenticationAuthorizationTests.cs`.

**Status**: Complete. Next unstarted item is **Day 24 — Event Bus**, pending explicit instruction
to begin.

### Day 24 — Event Bus

**Decision**: Built the real broker swap ADR-0012 (Day 18) explicitly anticipated for
`InMemoryEventBus` — **RabbitMQ** (confirmed with the user over Kafka's heavier footprint and
Azure Service Bus's deferred cloud commitment), **wired in behind
`AddCustomsClearanceMessaging()`** (not just an unwired container) via
`AddCustomsClearanceRabbitMqMessaging`, config-driven the same way as `DiagnosticsDb`/`IdentityDb`
(`RabbitMq:HostName` presence decides real broker vs. InMemory fallback). The real design problem:
a RabbitMQ consumer doesn't know `TPayload` at compile time the way `InMemoryEventBus`'s publish
call site does — solved with `EventTypeRegistry` (EventType string → CLR payload type) +
`DomainEventDispatcher` (reflection-based deserialize + dispatch), functionally identical to
`InMemoryEventBus`'s dispatch, just reflection-driven. `RabbitMqEventBus`/`RabbitMqEventConsumer`
publish/consume JSON envelopes via a durable topic exchange routed by `EventType`.

**Coverage approach**: extracted `DomainEventDispatcher` (the highest-risk logic — generic type
resolution + reflection invocation) as its own class and fully unit-tested it without a real
broker. `RabbitMqEventBus`/`RabbitMqEventConsumer` (real network I/O) were marked
`[ExcludeFromCodeCoverage]` with explicit justification rather than force-covered with hand-rolled
fakes for `RabbitMQ.Client`'s `IConnection`/`IModel` — this codebase has no mocking library
anywhere, by consistent pattern (same reasoning as `EfRepository`'s real-Postgres-only proof).
Proven instead via a live `docker compose` run and a new CI-enforced check.

**Real bug found and fixed**: the same class of startup race as Day 18's Postgres finding —
`docker-compose`'s `rabbitmq` healthcheck reports healthy once the Erlang node is up, before the
AMQP listener actually accepts connections; the Api crashed with `BrokerUnreachableException` on
first live run. Fixed with `RabbitMqConnectionHelper.CreateConnectionWithRetry` (10 attempts at the time, later 20 - see Day 25; 2s
backoff, synchronous since both call sites are eagerly-resolved constructors) — re-verified from a
genuinely fresh `docker compose down -v`/`up --build` cycle.

**Verification**: full publish→consume round trip proven live — same EventId/CorrelationId at
publish and at "HANDLED event" in the Api logs; RabbitMQ management API confirms the durable
exchange/queue with an active consumer. `build/scripts/smoke-test.sh` (the actual CI script)
extended with a real RabbitMQ round-trip check (polls Api logs for the handled EventId), passing
with unpiped exit code 0. Full-solution build/format/test all re-confirmed clean, unpiped.

**Files added**: `src/Platform/Messaging/` (`RabbitMqOptions.cs`, `EventTypeRegistry.cs`,
`DomainEventDispatcher.cs`, `RabbitMqEventBus.cs`, `RabbitMqEventConsumer.cs`,
`RabbitMqConnectionHelper.cs`); `tests/Unit/Platform.Messaging.Tests/`
(`DomainEventDispatcherTests.cs`, `ServiceCollectionExtensionsTests.cs`); `adr/0018-event-bus.md`;
`Phase-1_Architecture_Engineering_Prep/Day24_Execution_Log.md`. **Files modified**:
`src/Platform/Messaging/ServiceCollectionExtensions.cs`,
`src/Platform/Messaging/CustomsClearance.Platform.Messaging.csproj` (added
`Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Options.ConfigurationExtensions`,
`RabbitMQ.Client`), `src/Api/Program.cs`, `src/Api/appsettings.Development.json`,
`docker-compose.yml` (new `rabbitmq` service), `tests/Integration/Api.IntegrationTests/
TestApiFactory.cs`, `build/scripts/smoke-test.sh`.

**Status**: Complete. Next unstarted item is **Day 25 — Base Security Framework**, pending
explicit instruction to begin.

### Day 25 — Base Security Framework

**Decision**: Day 25's "wire security-lib into gateway" wording read as extending ADR-0016's Day
22 pass-through-only decision, not just re-proving it — confirmed with the user: the gateway now
performs its own **real JWT validation** (defense-in-depth), independent of the `Api` host's own
enforcement, not a replacement for it. `src/Gateway` gained a `Platform.Security` reference,
`AddCustomsClearanceAuthentication`/`AddCustomsClearanceAuthorization` +
`UseAuthentication`/`UseAuthorization`, and the same `"Jwt"` config section as the `Api` host (one
token validates at both). Per-route enforcement via **YARP's native `AuthorizationPolicy` route
config** (no custom middleware) — split the gateway's one catch-all route into a specific
`diagnostics-secure-route` (`AuthorizationPolicy: RequireAdmin`) ahead of the unchanged catch-all,
scoped to the one existing protected sample endpoint per the plan's own singular wording, not a
blanket "everything requires auth" policy (which would have blocked `/auth/login` itself).

**Verification — full defense-in-depth matrix proven live**: no token → 401 at the gateway; wrong
role (broker) → 403 at the gateway; tampered token (broken signature) → 401 at the gateway;
correct role (admin) → 200, passing both layers. Confirmed the rejections genuinely happen *at the
gateway* — the `Api` host's own audit log shows exactly one successful request, proving the other
three attempts never reached it. `/health`/`/auth/login` remained open throughout. CI's
`smoke-test.sh` (Postgres + RabbitMQ round-trips, Days 18/24) unaffected, full-solution
build/format/test clean, all with unpiped exit codes.

**Recurring finding (not new)**: Day 24's RabbitMQ startup-race retry budget (10×2s=20s)
occasionally insufficient — increased to 20×2s=40s in `RabbitMqConnectionHelper`, re-verified
across two fresh container cycles.

**Files added**: `adr/0019-base-security-framework.md`,
`Phase-1_Architecture_Engineering_Prep/Day25_Execution_Log.md`. **Files modified**:
`src/Gateway/CustomsClearance.Gateway.csproj`, `src/Gateway/Program.cs`,
`src/Gateway/appsettings.json`, `src/Gateway/Dockerfile`,
`src/Platform/Messaging/RabbitMqConnectionHelper.cs` (retry budget).

**Status**: Complete. Next unstarted item is **Day 26 — Structured Logging Rollout** (start of
Week 6, Observability), pending explicit instruction to begin.

### Day 26 — Structured Logging Rollout

**Decision**: `CorrelationIdMiddleware` (Day 16) was already wired into both running services —
the real gap was centralization, since both `Api` and `Gateway` still emitted plain-text console
output with nothing aggregating it. Confirmed with the user: **Grafana Loki + Promtail + Grafana**
(over ELK's heavier Elasticsearch footprint) and **.NET's built-in JSON console formatter** (over
adopting Serilog, a new dependency this codebase has never used). Added
`Platform.Logging.LoggingBuilderExtensions.AddCustomsClearanceStructuredLogging()` — one line in
each service's `Program.cs`. `docker-compose.yml` gained `loki`, `promtail` (Docker
service-discovery scraping, relabeled by `compose_service`), and `grafana` (Loki pre-provisioned
as a datasource via `observability/grafana-datasources.yaml`, so the log view is reproducible via
`docker compose up` alone — no manual click-through setup).

**Verification**: real JSON log output confirmed via `docker logs`; generated traffic tagged with
a known CorrelationId; **queried Loki directly** and got back both the publish and handle log
lines carrying that same CorrelationId — proving Promtail shipped logs and Loki indexed them
correctly; **queried through Grafana's own datasource proxy** (the actual view a user would use,
not just the Loki API) and got real results, with the datasource already provisioned with zero
manual setup. CI's `smoke-test.sh` and full-solution build/format/test all re-confirmed clean,
unpiped.

**Environmental issue found (not a design bug)**: a stray leftover Docker volume (from an earlier
interrupted session) caused RabbitMQ to crash at startup on the first fresh-environment attempt —
isolated via `docker volume prune`, confirmed unrelated to this day's actual changes, documented
in ADR-0020 since it briefly looked like a regression.

**Files added**: `src/Platform/Logging/LoggingBuilderExtensions.cs`,
`tests/Unit/Platform.Logging.Tests/LoggingBuilderExtensionsTests.cs`, `observability/`
(`promtail-config.yaml`, `grafana-datasources.yaml`), `adr/0020-structured-logging-and-log-
centralization.md`, `Phase-1_Architecture_Engineering_Prep/Day26_Execution_Log.md`. **Files
modified**: `src/Api/Program.cs`, `src/Gateway/Program.cs`, `docker-compose.yml`.

**Status**: Complete. Days 26-29 (Observability) are explicitly parallelizable per the Day-Wise
Plan, with Day 30 as the mandatory sequential wiring/review step. Next unstarted item is **Day 27
— Monitoring & Metrics**, pending explicit instruction to begin.

### Day 27 — Monitoring & Metrics

**Decision**: Confirmed **OpenTelemetry .NET + Prometheus exporter** over `prometheus-net` —
Microsoft's current recommended instrumentation path, built-in ASP.NET Core HTTP metrics with no
custom code. New shared `Platform.Observability` library (`AddCustomsClearanceMetrics(serviceName)`
+ `MapCustomsClearanceMetricsEndpoint()`), wired into both `Api` and `Gateway` the same way every
other `AddCustomsClearance*` cross-cutting concern is. `docker-compose.yml` gained `prometheus`
(scrapes both services' `/metrics` internally); Grafana (from Day 26) gained a second datasource
and a **provisioned dashboard** (`observability/dashboards/api-gateway-overview.json`) — 5 panels
covering Service Up, Request Rate, p95 Latency, Error Rate, Active Requests.

**Discipline point**: brought the stack up *first* and queried Prometheus's own
`/api/v1/label/__name__/values` to confirm the real OpenTelemetry-exported metric names before
writing a single dashboard query — semantic-convention metric names have shifted across
instrumentation versions, so guessing from docs risked a broken/empty panel that looks fine until
someone opens it.

**Verification**: both scrape targets reported healthy in Prometheus; generated real traffic; every
panel's exact PromQL query, run through Grafana's own datasource proxy (not Prometheus directly),
returned real non-zero data — including a plausible latency difference between gateway (~35ms,
its own Day 25 auth/rate-limit overhead) and api (~5ms). CI smoke-test and full-solution
build/format/test all clean, unpiped.

**Recurring issue, this time fully root-caused**: the Day 26 RabbitMQ startup crash recurred twice
more — traced fully this time (not just pruned) to an unrelated, months-old orphaned container
holding a colliding Docker volume, unconnected to this project. Removed explicitly; no
recurrence after. Documented as machine-specific leftover state, not a project config issue.

**Files added**: `src/Platform/Observability/` (csproj, `ServiceCollectionExtensions.cs`,
`ApplicationBuilderExtensions.cs`), `observability/prometheus.yml`,
`observability/grafana-dashboards-provider.yaml`, `observability/dashboards/api-gateway-
overview.json`, `adr/0021-monitoring-and-metrics.md`,
`Phase-1_Architecture_Engineering_Prep/Day27_Execution_Log.md`. **Files modified**:
`src/Api/Program.cs`, `src/Api/CustomsClearance.Api.csproj`, `src/Api/Dockerfile`,
`src/Gateway/Program.cs`, `src/Gateway/CustomsClearance.Gateway.csproj`, `src/Gateway/Dockerfile`,
`observability/grafana-datasources.yaml`, `docker-compose.yml`, `CustomsClearance.sln`.

**Status**: Complete. Next unstarted item was **Day 28 — Distributed Tracing**.

### Day 28 — Distributed Tracing

**Decision**: Confirmed **Grafana Tempo** over Jaeger — stays inside the existing Grafana stack
(Loki/Prometheus from Days 26/27) rather than adding a second query UI. `Platform.Observability`
gained `AddCustomsClearanceTracing(serviceName, otlpEndpoint)` — ASP.NET Core + HttpClient
instrumentation (the latter turns the Gateway's YARP-proxied call to `Api` into a real child span,
not two disconnected traces), every span tagged with the existing `X-Correlation-Id`
(`CorrelationIdMiddleware`, Day 16) via a new `Platform.Observability` → `Platform.Logging`
`ProjectReference`, exported via OTLP to Tempo. Both `Api`/`Gateway` wired the same way as every
other `AddCustomsClearance*` cross-cutting concern. `docker-compose.yml` gained `tempo`
(`observability/tempo.yaml`, local-disk single-binary config); Grafana gained a third datasource
with `tracesToLogsV2` pointed at Loki on the same `correlation_id` key, so a trace pivots straight
to its request's log lines.

**Verification**: real Gateway→Api request generated with a known correlation ID; Tempo's own
`/api/search?tags=correlation_id=...` found the trace; the full trace showed **three spans across
two services** (Gateway inbound, Gateway outbound/proxy hop, Api inbound) proving genuine
cross-service propagation; re-queried through **Grafana's own datasource proxy** (not Tempo
directly) with the same result. Prometheus's Day 27 scrape targets confirmed unaffected. CI
smoke-test and full-solution build/format/test all clean, unpiped.

**Recurring issue, same category as Days 26/27, resolved fast**: the first `docker compose up` hit
the same `.erlang.cookie: eacces` RabbitMQ crash — this time a leftover anonymous Docker volume
(not an orphaned container, per Day 27's finding), found via `docker ps -a`/`docker volume ls`
inspection and removed; RabbitMQ started healthy on the very next `up` with no further changes.

**Note**: `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.9.0 carries a `NU1902` moderate advisory
(disk-retry blob injection) — investigated and confirmed not exploitable here, since this project
never sets `OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY=disk`. Left un-upgraded to avoid breaking
compatibility with the pinned Prometheus exporter beta package (Day 27); documented in
`adr/0022-distributed-tracing.md` for future revisit.

**Files added**: `observability/tempo.yaml`, `adr/0022-distributed-tracing.md`,
`Phase-1_Architecture_Engineering_Prep/Day28_Execution_Log.md`. **Files modified**:
`src/Platform/Observability/ServiceCollectionExtensions.cs`,
`src/Platform/Observability/CustomsClearance.Platform.Observability.csproj`, `src/Api/Program.cs`,
`src/Gateway/Program.cs`, `observability/grafana-datasources.yaml`, `docker-compose.yml`.

**Status**: Complete. Next unstarted item was **Day 29 — Alerting Basics**.

### Day 29 — Alerting Basics

**Decision**: Confirmed **Grafana's built-in unified alerting** over a separate Prometheus
Alertmanager container — already running (Day 26's Grafana image), no new service, evaluates the
same datasource queries Day 27's dashboard already proved correct. Confirmed **no real
notification contact point** — no SMTP/webhook/Slack destination exists for this project yet
(ADR-0008/0015's deliberate no-cloud-hosting-decision-yet posture); alerts are proven via evaluated
**state** (Normal/Pending/Alerting) in Grafana's own Alerting UI/API, not a delivered notification.
`observability/alerting/rules.yaml` (Grafana's file-based alert provisioning, same pattern as
Days 26-28's datasources/dashboards) defines two rules, reusing Day 27's already-verified PromQL:
**Service Down** (`up{job=~"customsclearance-.*"}` < 1, `severity: critical`) and **High 5xx Error
Rate** (`severity: warning`). Prometheus datasource gained an explicit `uid: Prometheus` (matching
Tempo's Day 28 pattern) so rules reference it deterministically. One new read-only volume mount on
the existing `grafana` service — no new container.

**Verification — a genuine forced state transition, not just clean provisioning**: stopped the
`api` container; Prometheus's own `/api/v1/targets` confirmed the scrape target went down; waited
through the scheduler tick + rule's `for: 30s`; the rules API confirmed **`Service Down` went
`firing`/`Alerting`**, correctly labeled and timestamped. Restarted `api`; confirmed the rule
returned to `inactive`/Normal — full Normal→Alerting→Normal round trip proven, not one-way. No
RabbitMQ startup-race recurrence this day. CI smoke-test and full-solution build/format/test all
clean, unpiped.

**Files added**: `observability/alerting/rules.yaml`, `adr/0023-alerting-basics.md`,
`Phase-1_Architecture_Engineering_Prep/Day29_Execution_Log.md`. **Files modified**:
`observability/grafana-datasources.yaml`, `docker-compose.yml`.

**Status**: Complete. Days 26-29 (Observability) are all individually complete. Next unstarted item
was **Day 30 — the mandatory sequential wiring/review step** across all four.

### Day 30 — Observability Review

**Decision**: Reviewed Days 26-29 against the Day-Wise Plan's own Day 28 wording ("tracing across
gateway → service → **event bus**") and found a real gap: Day 28's tracing never actually
instrumented `RabbitMqEventBus`/`RabbitMqEventConsumer` (Day 24) — a request that triggered a
domain event dropped out of its trace at the point of publish. **Fixed as part of this review**
(Day 30 is explicitly the "wiring" step, not a passive audit): new `MessagingTelemetry`
`ActivitySource` in `Platform.Messaging` (built-in `System.Diagnostics.Activity`, no new package
dependency); `RabbitMqEventBus` now starts a Producer span and writes its W3C `traceparent` into a
RabbitMQ message header; `RabbitMqEventConsumer` reads it back and starts a Consumer span parented
to it — AMQP has no built-in trace-context carrier, so the header is the wire mechanism.
`AddCustomsClearanceTracing` gained `.AddSource(MessagingTelemetry.ActivitySourceName)` (without it
the OTel SDK silently ignores the custom source) — new `Platform.Observability` →
`Platform.Messaging` `ProjectReference`, and `src/Gateway/Dockerfile` gained a `COPY` step for
`Platform.Messaging` (the Gateway didn't previously build against it).

**Verification — one real request that also produced one real event**: `POST
/diagnostics/widgets` (chosen because it logs, gets scraped, publishes an event, and can be
traced — unlike a bare `/health` call). **Logs**: same `CorrelationId` found on both the "Published
event" and "HANDLED event" lines. **Traces**: one Tempo trace with **five spans** — Gateway
inbound → Gateway outbound (proxy) → Api inbound → `DiagnosticsWidgetCreated publish` (producer) →
`DiagnosticsWidgetCreated receive` (consumer) — the event-bus hop now genuinely part of the trace.
**Metrics**: Prometheus confirmed the real request was counted. **Alerts**: both Day 29 rules
stayed `inactive`/Normal against real traffic. All four signals cross-referenced by the same
correlation key for the same request/event — the literal deliverable. `Platform.Messaging`'s
coverage gate held (94.56%/80%) with no new test project needed. CI smoke-test and full-solution
build/format/test all clean, unpiped. No RabbitMQ startup-race recurrence this day.

**Files added**: `src/Platform/Messaging/MessagingTelemetry.cs`, `adr/0024-observability-review.md`,
`Phase-1_Architecture_Engineering_Prep/Day30_Execution_Log.md`. **Files modified**:
`src/Platform/Messaging/RabbitMqEventBus.cs`, `src/Platform/Messaging/RabbitMqEventConsumer.cs`,
`src/Platform/Observability/ServiceCollectionExtensions.cs`,
`src/Platform/Observability/CustomsClearance.Platform.Observability.csproj`,
`src/Gateway/Dockerfile`.

**Status**: Complete. Days 26-30 (Week 6, Observability) are fully complete as a cross-validated
set. Per the Day-Wise Plan, Week 7 (Days 31-35, Rules Engine Skeleton) is explicitly marked
non-parallelizable (a strict build chain). Next unstarted item was **Day 31 — Rules Engine: Storage
Model**.

### Day 31 — Rules Engine: Storage Model

**Decision**: New `Platform.RulesEngine` library implementing ADR-0002/ADR-0013's
Rule/RuleVersion/RuleCondition schema (Day 3 ER Diagram v0.2 §5) as real EF Core entities. `Rule`
(RuleCode unique, Category, Priority, Active — soft-deactivation only), `RuleVersion`
(effective-dated; `OutcomeFactName`/`OutcomeValue` generalizes the Day 19 spike's
clearance-type-only field, since later PRD stages react to arbitrary Facts flags a rule sets),
`RuleCondition` (Operator enum extended to all 6 ADR-0013 named — spike only had 4).
`RulesDbContext` uses the same `EnsureCreatedAsync`/`CreateTablesAsync`-fallback pattern as
Diagnostics/Identity (no EF Migrations pipeline in this codebase); unique indexes on `RuleCode` and
`(RuleId, VersionNumber)`; cascade deletes. Wired into `Api` the same connection-string-presence
convention as `DiagnosticsDb`/`IdentityDb`. New `Platform.RulesEngine.Tests` (InMemory provider,
95% core-tier coverage gate per ADR-0007 — Rules Engine is core business logic, not CRUD) achieved
97.14%/100%.

**Two real bugs found via live verification, not assumed from a clean build**: (1) repositories
accepted the abstract `PlatformDbContext`, which the Api host has only one DI registration for
(pointing at `DiagnosticsDbContext`) — silently resolved the wrong context, throwing "Cannot create
a DbSet for 'Rule'" on first insert; fixed by accepting the concrete `RulesDbContext` instead. (2)
returning the raw `Rule` entity graph from the diagnostic GET endpoint threw a JSON serialization
cycle (`Rule.Versions` ↔ `RuleVersion.Rule` bidirectional navigation); fixed by projecting to a DTO
— flagged as a discipline the real Day 34 admin API will also need.

**Verification**: `docker logs api` confirmed 5 real `CREATE TABLE` statements incl. both cascade
FKs; `POST`/`GET /diagnostics/rules` round-tripped a full Rule→RuleVersion→RuleCondition graph
after both fixes; direct `psql \d` inspection confirmed the unique indexes/FKs exist as real
Postgres constraints, not just C# annotations. Full-solution build/format/test and CI smoke-test
all clean, unpiped. No RabbitMQ startup-race recurrence.

**Files added**: `src/Platform/RulesEngine/` (csproj, `Rule.cs`, `RuleVersion.cs`,
`RuleCondition.cs`, `RuleCategory.cs`, `ConditionOperator.cs`, `RulesDbContext.cs`,
`RuleRepository.cs`), `tests/Unit/Platform.RulesEngine.Tests/`, `adr/0025-rules-engine-storage-
model.md`, `Phase-1_Architecture_Engineering_Prep/Day31_Execution_Log.md`. **Files modified**:
`src/Api/Program.cs`, `src/Api/CustomsClearance.Api.csproj`, `src/Api/Dockerfile`,
`src/Api/appsettings.Development.json`, `docker-compose.yml`,
`tests/Integration/Api.IntegrationTests/TestApiFactory.cs`, `CustomsClearance.sln`.

**Status**: Complete. Per the Day-Wise Plan's strict build chain, next unstarted item was **Day 32
— Rule Evaluation Pipeline**.

### Day 32 — Rules Engine: Evaluation Pipeline

**Decision**: Built four of ADR-0013's five pipeline interfaces against Day 31's schema:
`EfRuleSetLoader` (real EF Core query — Active + Category filter, then effective-dating selection,
`SingleOrDefault` so an authoring bug that leaves overlapping RuleVersions throws immediately
rather than silently picking one), `ConditionEvaluator` (all 6 operators ADR-0013 named — the Day
19 spike only had 4 — with numeric/date-aware comparison so `GreaterThan`/`LessThan`/`Between`
compare by magnitude, not text), `PriorityResolver` (lowest-Priority-wins, RuleCode tiebreak), and
`RulesEngine.EvaluateAsync` orchestrating the fixed 9-stage PRD Ch.5.7 order, mutating Facts in
place across stages. `IDecisionTraceRecorder`'s real persisted implementation stays Day 33's scope
per ADR-0013's own breakdown — Day 32 uses an in-memory placeholder, sufficient to prove the
pipeline. No real `IFactsProvider` built yet (no bounded context exists to assemble Facts from);
Facts is supplied directly by the caller.

**Coverage gate found real gaps, not just a number to chase**: first run landed at 92.79%
against the 95% core-tier gate; inspected the cobertura report directly and added targeted tests
for `ConditionEvaluator`'s fallback branches (including an out-of-range enum cast to hit the
`NotSupportedException` default case), the `RuleVersion.Rule`/`RuleCondition.RuleVersion` inverse
navigation setters (not reliably attributed via EF's automatic fixup), and
`ServiceCollectionExtensions` (untested DI wiring). Reached 100%/97.53%.

**Verification — live against real PostgreSQL, not just InMemory-provider unit tests**: new `POST
/diagnostics/rules/evaluate?shipmentValue=X` endpoint seeds one real De Minimis rule if absent,
then runs the actual `RulesEngine` against it. `shipmentValue=2000` (under threshold) →
`clearanceType: "DEM"` with a full 9-step trace showing the winning rule; `shipmentValue=999999`
(over threshold) → `clearanceType: null`, every stage "no match". Direct `psql` query confirmed the
seeded rule genuinely persisted and was loaded through `EfRuleSetLoader`'s real query. 45 unit
tests total; full-solution build/format/test and CI smoke-test all clean, unpiped.

**Recurring RabbitMQ startup issue, same category as Days 26-30**: this time traced to a stale
volume belonging to a previous run's own crashed `rabbitmq` container (not an unrelated orphan) —
removed container + volume together; resolved on the next `up`.

**Files added**: `src/Platform/RulesEngine/` (`Facts.cs`, `EvaluationContext.cs`,
`PipelineStages.cs`, `IRuleSetLoader.cs`, `EfRuleSetLoader.cs`, `IConditionEvaluator.cs`,
`ConditionEvaluator.cs`, `IPriorityResolver.cs`, `PriorityResolver.cs`,
`IDecisionTraceRecorder.cs`, `RulesEngine.cs`, `ServiceCollectionExtensions.cs`), 5 new test files
in `tests/Unit/Platform.RulesEngine.Tests/`, `adr/0026-rules-engine-evaluation-pipeline.md`,
`Phase-1_Architecture_Engineering_Prep/Day32_Execution_Log.md`. **Files modified**:
`src/Api/Program.cs`, `tests/Unit/Platform.RulesEngine.Tests/CustomsClearance.Platform.RulesEngine.Tests.csproj`.

**Status**: Complete. Per the Day-Wise Plan's strict build chain, next unstarted item was **Day 33
— Rules Engine: Versioning & Decision Trace**.

### Day 33 — Rules Engine: Versioning & Decision Trace

**Decision**: Versioning itself was already built Day 31; Day 33's real scope was replacing Day
32's `InMemoryDecisionTraceRecorder` placeholder with real, persisted trace capture. New
`DecisionTrace` entity — one row per pipeline stage per evaluation, grouped by an indexed
`EvaluationId`, `RuleId`/`RuleVersionId` nullable with `OnDelete(SetNull)` (not cascade — a trace
row is an audit record that must outlive the Rule/RuleVersion it referenced, ADR-0004),
`FactsSnapshotJson` storing the open-ended Facts bag as JSON text. Corrected a Day 32 deviation
from ADR-0013's own spec along the way: `IDecisionTraceRecorder.Flush()` was sync; the original
design always specified async `FlushAsync` — fixed now that persistence needs `SaveChangesAsync`.
`EfDecisionTraceRecorder` buffers rows per step, one `SaveChangesAsync` per evaluation.
`RulesEngine.EvaluateAsync` now generates and returns an `EvaluationId` per run. New `GET
/diagnostics/rules/trace/{evaluationId}` endpoint proves the trace is independently queryable.

**Verification — the literal deliverable, live against real PostgreSQL**: evaluated a real seeded
rule, then queried its trace back out by `EvaluationId` — **9 real persisted rows**, correctly
ordered, winning stage showing real `RuleId`/`RuleVersionId`, `FactsSnapshotJson` correctly
capturing the cross-stage `ClearanceType` mutation from stage 5 onward. Direct `psql` query
confirmed 18 rows (two evaluations) and the real `ON DELETE SET NULL` FK constraints. 50 unit
tests (up from 45), 100%/97.75% coverage. Full-solution build/format/test and CI smoke-test all
clean, unpiped.

**Real environmental issue, different root cause than Days 26/27/30**: the familiar
`.erlang.cookie: eacces` RabbitMQ crash recurred against a *provably fresh* volume this time (not
stray leftover state) — root-caused instead to a corrupted local copy of the
`rabbitmq:3.13-management-alpine` image itself; `docker rmi -f` + re-pull resolved it permanently.
Restarting Docker Desktop alone did not fix it, confirming the image was the actual cause.

**Files added**: `src/Platform/RulesEngine/DecisionTrace.cs`,
`src/Platform/RulesEngine/EfDecisionTraceRecorder.cs`,
`tests/Unit/Platform.RulesEngine.Tests/EfDecisionTraceRecorderTests.cs`,
`adr/0027-rules-engine-versioning-and-decision-trace.md`,
`Phase-1_Architecture_Engineering_Prep/Day33_Execution_Log.md`. **Files modified**:
`src/Platform/RulesEngine/RulesDbContext.cs`, `src/Platform/RulesEngine/IDecisionTraceRecorder.cs`,
`src/Platform/RulesEngine/RulesEngine.cs`, `src/Platform/RulesEngine/ServiceCollectionExtensions.cs`,
`src/Api/Program.cs`.

**Status**: Complete. Per the Day-Wise Plan's strict build chain, next unstarted item was **Day 34
— Rules Engine: Admin API (minimal)**.

### Day 34 — Rules Engine: Admin API (Minimal)

**Decision**: Confirmed with the user — `/api/rules/*` requires Admin authorization
(`PlatformPolicies.RequireAdmin`, same as `/diagnostics/secure`), and changing a rule's conditions/
outcome goes through a separate "add new version" endpoint, not an in-place `PUT` overwrite (matches
the effective-dated design). New `src/Api/RulesAdmin/RuleAdminEndpoints.cs` — create/list/get/
update/deactivate/add-version, enforcing the two invariants ADR-0013/ADR-0025 explicitly deferred
to this day: RuleCode uniqueness and no overlapping RuleVersion effective-date windows. The
overlap/supersede logic was extracted into `RuleVersionEffectiveDateValidator`
(`Platform.RulesEngine`) rather than left inline, so it's covered by that project's 95% gate.

**Two real bugs found and fixed via first live test**: (1) `System.Text.Json`'s default enum
converter rejected string values (`"category": "DeMinimis"`) sent by a real client — fixed by
registering `JsonStringEnumConverter` globally, since the deliverable's own wording ("testable via
Postman/Swagger") means humans typing strings, not raw enum integers. (2) The resulting
`BadHttpRequestException` fell through to a generic 500 instead of a real 400 — fixed with a new
`ErrorMapper` branch (benefits every endpoint in the Api host, not just this one), with unit tests
added for both the mapper and the full middleware pipeline.

**One real workflow gap found and fixed, confirmed with the user**: adding a new RuleVersion after
an existing open-ended one was rejected as an "overlap" (correct per the literal invariant, but
left no way to ever supersede a rule's policy). Confirmed **auto-close the prior open-ended
version** — the standard effective-dating pattern, doesn't rewrite history since past evaluations/
DecisionTrace rows are unaffected. 8 new unit tests cover the supersede case and genuine-conflict
edge cases.

**Verification — closing the loop across the whole week**: full live JWT-authenticated CRUD flow
(401/201/422/200 all correct), then **a real evaluation via Day 32's pipeline confirmed a
deactivated rule no longer fires** — Days 31-34 proven working together, not just in isolation.

**RabbitMQ startup race, finally root-caused and fixed for good**: recurred again (even against a
named volume, tried first and alone insufficient); a plain `docker start` retry against the same
already-created volume succeeded every time, proving a one-time first-boot race rather than
corrupted/stray state. Fixed with `restart: on-failure:5` on the rabbitmq service plus a 3-attempt
retry wrapper around `docker compose up` in `build/scripts/smoke-test.sh` — verified clean across 6
consecutive fresh cycles, the first self-healing fix in this recurring saga (Days 26/27/30/33).

**Files added**: `src/Api/RulesAdmin/RuleAdminEndpoints.cs`,
`src/Api/RulesAdmin/RuleAdminModels.cs`,
`src/Platform/RulesEngine/RuleVersionEffectiveDateValidator.cs`,
`tests/Unit/Platform.RulesEngine.Tests/RuleVersionEffectiveDateValidatorTests.cs`,
`adr/0028-rules-engine-admin-api.md`,
`Phase-1_Architecture_Engineering_Prep/Day34_Execution_Log.md`. **Files modified**:
`src/Platform/ErrorHandling/ErrorMapper.cs`,
`src/Platform/ErrorHandling/ExceptionHandlingMiddleware.cs`, `src/Api/Program.cs`,
`docker-compose.yml`, `build/scripts/smoke-test.sh`, plus new tests in
`Platform.ErrorHandling.Tests`.

**Status**: Complete. Per the Day-Wise Plan's strict build chain, next unstarted item was **Day 35
— Rules Engine Review**.

### Day 35 — Rules Engine Review

**Decision**: Found a real gap before any rule could be usefully evaluated — every existing
evaluation entry point (`POST /diagnostics/rules/evaluate`, Day 32) only ever supplied one
hardcoded fact (`ShipmentValue`), which cannot prove ADR-0013's central design claim that `Facts`
is deliberately mutable in place so a later pipeline stage reacts to an earlier stage's outcome.
Added `POST /diagnostics/rules/evaluate-facts` (accepts an arbitrary fact dictionary, runs the
real `RulesEngine` pipeline) alongside the unchanged Day 32 endpoint. Loaded **three real
India-specific sample rules through the actual Day 34 admin API** (not hand-seeded): `IN-DEM-
GIFT-001` (De Minimis — individual gift imports under INR 5000, reflecting India's CBIC
gift-import policy), `IN-HS-CONTROLLED-9018` (HS Code 9018 medical/surgical instruments — import-
restricted, sets `ControlledGoodsFlag`), and `IN-PERMIT-CONTROLLED-001` (Permit — fires only when
`ControlledGoodsFlag` is set by an earlier stage, the deliberate proof of cross-stage chaining).

**Verification — first real proof of ADR-0013's mutable-Facts design, not just each day's own
isolated proof**: Scenario A (individual gift, value 2500) → `ClearanceType: "DEM"` via
`IN-DEM-GIFT-001`. Scenario B (commercial, HS code 9018, value 50000) → `ControlledGoodsFlag:
"true"` at the HS Code stage **and** `PermitRequired: "true"` at the later Permit stage — the
Permit rule fired purely because an earlier stage mutated Facts, something a single-fact evaluate
call could never demonstrate. Both evaluations' persisted decision traces independently re-queried
via the Day 33 trace endpoint and a direct `psql` count — 9 real rows each, correct `RuleId`/
`RuleVersionId` on the winning steps. Deactivated `IN-DEM-GIFT-001` via the Day 34 admin API and
re-ran Scenario A — `ClearanceType` correctly stopped firing, closing the loop across all five
days again. RabbitMQ's Day 34 self-healing fix (`restart: on-failure:5`) was exercised for real
this session (first `docker compose up` hit the first-boot race; a plain retry succeeded with zero
manual intervention). Full-solution build/format/test and the actual CI `smoke-test.sh` all clean,
unpiped.

**Files added**: `adr/0029-rules-engine-review.md`,
`Phase-1_Architecture_Engineering_Prep/Day35_Execution_Log.md`. **Files modified**:
`src/Api/Program.cs` (new `evaluate-facts` diagnostic endpoint).

**Status**: Complete. Week 7 (Days 31-35, Rules Engine Skeleton) is fully complete as a
cross-validated set. Next unstarted item is whatever Phase 0 schedules next, pending explicit
instruction to begin.

### Day 36 — Configuration Service

**Decision**: Settled four design questions with the user before building. New
`Platform.Configuration` library inside the `Api` host (not a separate service, per ADR-0003).
`ConfigEntry` is a PostgreSQL typed key/value row (unique `Key`, text `Value`, `ValueType`
String/Integer/Decimal/Boolean, `Category`, `IsFeatureFlag`), created by the same
EnsureCreated/CreateTables startup pattern as Rules/Identity and registered only when
`ConnectionStrings:ConfigDb` is present (route mapping behind the same guard). `IConfigService`
reads through a 30s `IMemoryCache`; admin writes call `Invalidate(key)` so a change shows
immediately on the host. Admin-gated (`RequireAdmin`) `/api/config/*` CRUD; `PUT` returns and logs
old/new value for Day 38's audit wiring. `Key`/`ValueType` are immutable after creation.

**Verification**: live through the Gateway against real Postgres — real `CREATE TABLE` in the Api
log plus `psql \d`; 401/403/201 auth matrix; threshold changed 5000 → 10000 via `PUT` and read
back as 10000 immediately with the `api` container never restarted; 422/404/204 negative paths.
24 unit tests (97.36% line / 100% branch vs the 80% gate). Full-solution build/format/test and the
CI `smoke-test.sh` all exit 0, unpiped.

**Environmental finding**: host port 5432 was held by an unrelated project's container
(`saferte_postgres`); left running, and worked around with a temporary compose override outside
the repo that remaps only postgres's host port. The repo's `docker-compose.yml` is unchanged.

**Files added**: `src/Platform/Configuration/` (csproj, `ConfigEntry.cs`, `ConfigValueType.cs`,
`ConfigDbContext.cs`, `ConfigValueValidator.cs`, `IConfigService.cs`, `ConfigService.cs`,
`ConfigCacheOptions.cs`, `ServiceCollectionExtensions.cs`); `src/Api/ConfigAdmin/`
(`ConfigAdminEndpoints.cs`, `ConfigAdminModels.cs`); `tests/Unit/Platform.Configuration.Tests/`;
`adr/0030-configuration-service.md`; `Phase-1_Architecture_Engineering_Prep/Day36_Execution_Log.md`.
**Files modified**: `src/Api/Program.cs`, `src/Api/CustomsClearance.Api.csproj`,
`src/Api/Dockerfile`, `src/Api/appsettings.Development.json`, `docker-compose.yml`,
`tests/Integration/Api.IntegrationTests/TestApiFactory.cs`, `CustomsClearance.sln`.

**Status**: Complete (not yet committed). Day 37 (Notification Base) is independent of Day 36;
Day 38 (Audit Trail End-to-End) depends on it. Next unstarted item is **Day 37**, pending explicit
instruction to begin.

### Day 37 — Notification Base

**Decision**: Settled four design questions with the user before building. New
`Platform.Notifications` library inside the `Api` host (ADR-0003). A `NotificationRequested` domain
event (published over the Day 24 RabbitMQ bus, registered in `EventTypeRegistry`) is handled by
`NotificationRequestedHandler` → `NotificationDispatcher`, which persists one `Notification` (the
in-app record; `ReadAt` for read state) and runs each applicable `INotificationChannel`
(`InApp`, `Email`), recording one `NotificationDelivery` row per channel
(Delivered/Failed/Skipped). A failing channel never loses the in-app record or blocks other
channels. Email goes through `IEmailSender`: real SMTP (`SmtpClient`) when `Email:Host` is set —
a new `mailpit` container in `docker-compose.yml` is the dev SMTP sink (UI/API on 8025) — else
`DisabledEmailSender` and the channel records Skipped. `Notification.SourceEventId` (unique) makes
RabbitMQ redelivery idempotent. Read API: `GET /api/notifications/mine`, `POST
/api/notifications/{id}/read` (owner only; others get 404), Admin-only `GET /api/notifications`; no
create endpoint by design (events only). Dev-only `POST /diagnostics/notifications/test-event`
fires the sample event. Everything sits behind `ConnectionStrings:NotificationsDb` presence.

**Verification**: live through the Gateway against real Postgres/RabbitMQ/Mailpit — 3 real events
→ 3 notifications with `SourceEventId` == published `eventId`, exactly the 2 expected emails in
Mailpit; auth/ownership matrix (401/403/404/200); email-failure isolation proven by stopping
Mailpit (`Email=Failed`, `InApp=Delivered`). 19 unit tests (99.34% line / 100% branch vs the 80%
gate). Full-solution build/format/test exit 0; `smoke-test.sh` extended with an event →
notification → SMTP check and run from a fresh-volume cycle, exit 0 — all unpiped.

**Environmental notes**: host port 5432 still held by an unrelated project's `saferte_postgres`
(temporary compose override outside the repo, as on Day 36); the known RabbitMQ first-boot race
recurred once and self-healed on a plain second `up` (ADR-0028 policy).

**Files added**: `src/Platform/Notifications/` (csproj, `Notification.cs`,
`NotificationsDbContext.cs`, `Channels.cs`, `SmtpEmailSender.cs`, `NotificationDispatcher.cs`,
`NotificationRequestedHandler.cs`, `ServiceCollectionExtensions.cs`);
`src/Api/Notifications/NotificationEndpoints.cs`; `tests/Unit/Platform.Notifications.Tests/`;
`adr/0031-notification-base.md`; `Phase-1_Architecture_Engineering_Prep/Day37_Execution_Log.md`.
**Files modified**: `src/Api/Program.cs`, `src/Api/CustomsClearance.Api.csproj`,
`src/Api/Dockerfile`, `src/Api/appsettings.Development.json`, `docker-compose.yml`,
`build/scripts/smoke-test.sh`, `tests/Integration/Api.IntegrationTests/TestApiFactory.cs`,
`CustomsClearance.sln`.

**Status**: Complete (not yet committed). Deferred: retrying Failed deliveries, templates, user
preferences, resolving real recipient addresses from a user profile (the address currently travels
in the event payload — personal data under DPDPA 2023, ADR-0004), a real SMTP relay. Next
unstarted item is **Day 38 — Audit Trail End-to-End** (depends on Day 36's config service, which
exists), pending explicit instruction to begin.

### Day 38 — Audit Trail End-to-End

**Decision**: Settled four design questions with the user before building. `Platform.Audit` (Day 17)
only had a console writer, so the real gap was a durable, queryable store. New
`Platform.Audit.Persistence` project (keeps `Platform.Audit` storage-agnostic and EF-free):
`AuditDbContext` (plain `DbContext`, **rejects update/delete in `SaveChanges`** — application-level
append-only), `AuditEventRecord` (FieldChanges as a JSON text column), `EfAuditWriter` (singleton
over `IDbContextFactory`, so on its own connection), `AuditQueryService`, and
`AddCustomsClearanceAuditPersistence(o => o.UseNpgsql(...))` which *replaces* the
`ConsoleAuditWriter` registration (provider supplied by the caller, ADR-0001). Active when
`ConnectionStrings:AuditDb` is set; otherwise audit stays on the console (integration tests stay
infrastructure-free). Config create/update/delete (Day 36) now write `TechnicalAudit`/`Internal`
events with field-level old/new values and `PerformedBy` = JWT `sub`; unchanged or rejected PUTs are
not audited. `GET /api/audit` (+ `/{id}`) is `RequireComplianceOfficerOrAdmin`, filtered, paged,
newest first; read-only by design.

**Accepted, documented gaps**: (1) DB-level immutability — an `UPDATE` on `AuditEvents` succeeds at
the database (shown live); deferred to Day 41 Security Hardening. (2) Change and audit are
sequential, not atomic — a crash between the two commits could lose the audit row; an audit failure
returns 500 after the change is committed. (3) No retention/partitioning (ADR-0004: 5 years,
monthly) or tamper-evidence yet. Other mutations (e.g. Day 34 rules admin) are not audited yet.

**Verification**: live through the Gateway on the **unmodified** `docker-compose.yml` (port 5432 was
freed) — admin created/changed/deleted a config value; a compliance officer read back 3 events with
old/new values, timestamps and the admin's user id (matches `Users.Id`); no-op/invalid PUTs left no
rows; access matrix 200/200/403/403/401; response `X-Correlation-Id` equals the stored audit
`CorrelationId`. 13 unit tests (100% line/branch vs the 95% gate). Full-solution build/format/test
and the extended `smoke-test.sh` (config change → `/api/audit` old/new check, fresh volumes) all exit
0, unpiped.

**Side-finding (not changed)**: the Gateway response carries `X-Correlation-Id` twice with the same
value.

**Files added**: `src/Platform/AuditPersistence/` (csproj, `AuditEventRecord.cs`, `AuditDbContext.cs`,
`EfAuditWriter.cs`, `AuditQueryService.cs`, `ServiceCollectionExtensions.cs`);
`src/Api/AuditTrail/AuditEndpoints.cs`; `src/Api/ConfigAdmin/ConfigAudit.cs`;
`tests/Unit/Platform.Audit.Persistence.Tests/`; `adr/0032-audit-trail-end-to-end.md`;
`Phase-1_Architecture_Engineering_Prep/Day38_Execution_Log.md`. **Files modified**:
`src/Api/ConfigAdmin/ConfigAdminEndpoints.cs`, `src/Api/Program.cs`,
`src/Api/CustomsClearance.Api.csproj`, `src/Api/Dockerfile`, `src/Api/appsettings.Development.json`,
`docker-compose.yml`, `build/scripts/smoke-test.sh`,
`tests/Integration/Api.IntegrationTests/TestApiFactory.cs`, `CustomsClearance.sln`.

**Status**: Complete (not yet committed). Next unstarted item is **Day 39 — Seed Data & Reference
Tables** (parallelizable per the plan), pending explicit instruction to begin.

### Day 39 — Seed Data & Reference Tables

**Decision**: Settled four design questions with the user before building. New
`Platform.ReferenceData` library inside the `Api` host (`Country` keyed by ISO alpha-2 + unique
alpha-3, `Currency` by ISO 4217 + numeric code + minor units, `UnitOfMeasure` by India UQC), all
on a `ReferenceEntity` base with `Code`, `Name`, `IsActive`, **`Source` and `IsVerified`**. Stub-size
datasets only (49 countries, 13 currencies, 45 units) in embedded `Data/*.json`;
`ReferenceDataSeeder` is **insert-missing-by-code**, so every startup is safe and edited/deactivated
rows are never overwritten. Read-only `GET /api/reference/{countries|currencies|units}` (+ `/{code}`,
case-insensitive, `?includeInactive=true`) for any authenticated user; no write API until Phase 1
master-data work. Registration, table creation + seeding, and routes sit behind
`ConnectionStrings:ReferenceDataDb`.

**Data accuracy caveat (important)**: the datasets were written from recall of ISO 3166-1, ISO 4217
and the ICEGATE UQC list and were **not checked against an official source**. Tests prove structure
(formats, uniqueness, INR/JPY decimals, provenance markers), not that each name/code is correct. Every
row is therefore `Source=Placeholder-Day39`, `IsVerified=false`, visible in every API response.
Verifying against official masters (especially UQC) is a Phase 1 task, alongside ITC-HS sourcing.

**Verification**: live through the Gateway on the unmodified `docker-compose.yml` — Api log
`49 countries, 13 currencies, 45 units inserted`; `psql` shows 49/13/45 rows with unique indexes;
401/200/404/405 API behaviour; a row deactivated and renamed directly in Postgres survived an Api
restart (`0/0/0 inserted`). 11 unit tests (100% line vs the 80% gate), including tests over the data
files. Full-solution build/format/test and the extended `smoke-test.sh` (reads `countries/IN`,
`currencies/INR`, `units/KGS`; fresh volumes) all exit 0, unpiped. The known RabbitMQ first-boot race
recurred once and cleared on a plain second `up`.

**Files added**: `src/Platform/ReferenceData/` (csproj, `ReferenceEntities.cs`,
`ReferenceDataDbContext.cs`, `ReferenceDataSeeder.cs`, `Data/{countries,currencies,units}.json`);
`src/Api/ReferenceData/ReferenceDataEndpoints.cs`; `tests/Unit/Platform.ReferenceData.Tests/`;
`adr/0033-seed-data-and-reference-tables.md`;
`Phase-1_Architecture_Engineering_Prep/Day39_Execution_Log.md`. **Files modified**:
`src/Api/Program.cs`, `src/Api/CustomsClearance.Api.csproj`, `src/Api/Dockerfile`,
`src/Api/appsettings.Development.json`, `docker-compose.yml`, `build/scripts/smoke-test.sh`,
`tests/Integration/Api.IntegrationTests/TestApiFactory.cs`, `CustomsClearance.sln`.

**Status**: Complete (not yet committed). Next unstarted item is **Day 40 — Integration Smoke Test**
(auth → gateway → sample service → rules engine → audit → notification; a single sequential
verification pass), pending explicit instruction to begin.

### Day 40 — Integration Smoke Test

**Decision**: Closes Week 8 (Configuration & Notification Base). The user returned the first set of
design questions for clarification; the open reporting questions were then asked again on their own.
Decisions: **verify-only** (no glue code linking rules → audit → notification), a **new**
`build/scripts/e2e-smoke-test.sh` with its **own CI job** (`e2e-smoke-test` in
`.github/workflows/ci.yml`, `E2ESmokeTest` stage in `build/azure-pipelines.yml`, lockstep per ADR-0005)
rather than extending `smoke-test.sh`, **happy path only**, a numbered PASS/FAIL log plus an **evidence
file** (`TestResults/e2e-evidence.txt`, uploaded as a CI artifact), and **stop at the first failure**
with a container-log dump. One `X-Correlation-Id` is sent on every request. 20 checks: environment;
admin + broker login; Gateway liveness/proxy/dual-layer Admin JWT/correlation echo; widget
write+read and its RabbitMQ event handled with our correlation ID; rule created via the admin API →
evaluated to `ClearanceType=DEM` → 9 persisted trace rows; config change audited with old/new value,
user and our correlation ID; `NotificationRequested` → notification with `Email=Delivered,InApp=Delivered`
→ visible to the broker → email at the SMTP server. Day 48's regression pass is meant to re-run this.

**Verification**: before starting, all six latest GitHub Actions runs (Days 34-39) were green. The
script passed 20/20 (exit 0) on clean `down -v` environments, and — because a test that has only ever
passed hasn't proven it can fail — a **fault injection** (dead SMTP-inbox URL) produced `FAIL [20]`,
stopped with 19 passes, dumped logs and exited 1. That run exposed a gap (no outcome in the evidence
file), fixed so a failure now writes `checks_passed`, `failed_check` and `result=FAIL`; the final pass
and fail runs were repeated after the fix. YAML validated; `bash -n` clean. No product code changed.

**Honest scope**: this proves the pieces work together through the real edge and that a correlation ID
survives the Gateway, event bus, audit store and publish log. It does **not** prove one business action
flows through rules → audit → notification (no such wiring exists), and the correlation ID is not
stored on decision-trace rows or the notification record. Observability signals and Day 37-39 negative
behaviours are not re-verified. **CI finding**: the new job's first GitHub run failed at check 17 (an unpolled one-shot read of
`docker compose logs` raced the runner's log delivery); the check now polls and the failure dump uses
`--tail=500`. Fixed and passing locally 20/20, but checks 18-20 have not yet run on CI, so the next CI
run is the real confirmation. Lesson: a local pass can hide a timing race; poll, don't read logs once.

**Files added**: `build/scripts/e2e-smoke-test.sh`; `adr/0034-phase-0-end-to-end-smoke-test.md`;
`Phase-1_Architecture_Engineering_Prep/Day40_Execution_Log.md` (embeds the passing evidence).
**Files modified**: `.github/workflows/ci.yml`, `build/azure-pipelines.yml`, `CLAUDE.md`.

**Status**: Complete (not yet committed). Week 8 (Days 36-40) is fully complete. Next unstarted item is
**Week 9, Day 41 — Security Hardening** (Days 41-44 are independent workstreams, a parallelization
candidate), pending explicit instruction to begin. Known carry-forwards for Day 41: database-level audit
immutability (ADR-0032), personal data in the notification event payload (ADR-0031), the duplicate
`X-Correlation-Id` response header from the Gateway.

### Parallel-work Foundation (between Day 40 and Day 41)

**Decision**: Step 1 of the multi-agent adoption plan, structure only (ADR-0035). `src/Api/Program.cs`
(863 lines) became ~125: each capability is now an `IApiModule` in `src/Api/Modules/` that owns its service
registration, Development schema bootstrap and routes, discovered by assembly scan - adding one means
adding a file, not editing `Program.cs`. `DatabaseBootstrap` holds the retry + EnsureCreated/CreateTables
logic once. `docker-compose.yml` host ports and image tags are overridable with unchanged defaults, and
`build/scripts/agent-env.sh <slot>` gives each worker its own compose project and port range.
`docs/PARALLEL-WORK.md` lists the files that stay shared and who owns them.

**Verification**: build/format/full tests exit 0; Day 40 E2E 20/20 on two stacks started together (default
and `cc-slot-1`); unmodified `smoke-test.sh` exit 0; 110/110 slot ports distinct (the first check was
vacuous and a real SMTP-port collision was found by the second). Not committed.

**Status**: Complete (not yet committed). Next steps of the adoption plan (skills, Days 41-44 independence
check, pilot) not started; Day 41 awaits explicit instruction.

## Day-by-Day Deliverables Log (Phase 0, Week 9: Hardening & Documentation - Days 41-44)

| Day | Deliverable | Key artifacts | ADR |
|---|---|---|---|
| 41 | Security Hardening - DB-level audit immutability (restricted role), JWT key guard, single correlation header, OpenTelemetry upgrade, Phase 0 security checklist | `adr/0036-*.md`, `docs/security/PHASE0_SECURITY_CHECKLIST.md`, `Day41_Execution_Log.md` | ADR-0036 |
| 42 | Performance Baseline - configurable Gateway rate limit, k6 load scripts, first baseline report | `adr/0037-*.md`, `docs/performance/BASELINE-2026-10-19.md`, `tests/Performance/`, `build/scripts/perf-baseline.sh`, `Day42_Execution_Log.md` | ADR-0037 |
| 43 | CI/CD Hardening - dependency scan, secret scan, CodeQL, Dependabot, `CI passed` aggregate, branch-protection runbook | `adr/0038-*.md`, `docs/BRANCH-PROTECTION.md`, `Day43_Execution_Log.md` | ADR-0038 |
| 44 | Documentation skeleton - architecture overview, C4, RUN-LOCALLY / ADD-A-MODULE / DEPLOY runbooks | `adr/0039-*.md`, `docs/Architecture/`, `docs/C4/`, `docs/runbooks/`, `Day44_Execution_Log.md` | ADR-0039 |

Days 41, 43 and 44 were built in parallel by three worktree agents (the multi-agent pilot, ADR-0035) and
integrated by the orchestrator; Day 42 is deliberately sequential after Day 41.

### Day 41 - Security Hardening

**Decision**: Audit immutability is enforced by the database, not a trigger (ADR-0001 portability): the
Audit module's runtime connection is the restricted role `audit_writer` (SELECT and INSERT on `"AuditEvents"`
only, plain GRANT/REVOKE); a separate owner connection `ConnectionStrings:AuditAdminDb` creates the table,
role and grants in the Development bootstrap only. `JwtKeyValidator`: outside Development both hosts refuse
the committed placeholder key or one under 32 bytes. `CorrelationIdMiddleware` now sets the response header at
response start, so the Gateway no longer returns `X-Correlation-Id` twice. OpenTelemetry moved 1.9.0 to 1.15.x
as one set (1.11.2 was still flagged), clearing NU1902; the build now has 0 warnings.

**Real finding**: both Dockerfiles baked in `ASPNETCORE_ENVIRONMENT=Development`, so a deployed image would
have run with the placeholder key, seeded users and Swagger. They now default to `Production`; compose sets
Development explicitly.

**Accepted / open** (full list in `docs/security/PHASE0_SECURITY_CHECKLIST.md`): the recipient address is still
in the notification event payload (only SMTP error text is redacted); table owner or a superuser can still
alter audit rows; no TLS, security headers or login lockout; `/metrics` reachable through the Gateway; 8-hour
tokens (Day 10 says 15 minutes); MFA, retention and DPDPA handling need decisions.

**Verification**: build 0 warnings; unit and integration tests exit 0; `smoke-test.sh` exit 0 with a new check
that `audit_writer` can SELECT but UPDATE/DELETE/TRUNCATE are refused; E2E 20/20 (check 7 now asserts exactly
one correlation header).

### Day 43 - CI/CD Hardening

**Decision**: Before today CI had no dependency scan, secret scan, CodeQL or aggregate check, and
`CONTRIBUTING.md` wrongly said `main` was protected. Added `build/scripts/dependency-scan.sh` (High/Critical
fail, Moderate/Low warn, refuses to report clean if advisory data is missing), `build/scripts/secret-scan.sh`
(gitleaks over full history; `.gitleaks.toml` allowlists only the two dev placeholders by exact value),
`.github/workflows/codeql.yml` (no Azure equivalent, documented), `.github/dependabot.yml`, and a `CI passed`
aggregate job over nine gating jobs; `azure-pipelines.yml` kept in lockstep. The first scan found 3 real High
advisories in test-only transitive packages, fixed with pins in `tests/Directory.Build.props` rather than
exempted.

**Verification**: each gate was shown to fail on a deliberately broken change in a scratch clone (failing test
exit 1, lint exit 2, coverage drop exit 1, vulnerable package exit 1, secret-shaped strings exit 1) and to pass
on the clean tree. All the new jobs then ran green on GitHub on the integration push (CI 11/11, CodeQL
`Analyze (csharp)`). **Not proven**: the Azure YAML has never run anywhere.

**Update 2026-10-19 - branch protection enabled** (`docs/BRANCH-PROTECTION.md`, payload as documented: PR
required, checks `CI passed` + `Analyze (csharp)`, `enforce_admins` true, 0 approvals, no force-push or
deletion, linear history). Proven on real GitHub with demo PR #20 (closed unmerged): a failing test made
`CI passed` red, the merge was refused (`BLOCKED`), and a direct push to `main` was rejected (GH006). From
here **every change to `main` goes through a PR** (squash or rebase). The working-agreement rule "commit/push
only when asked" still applies; the push is now a branch plus a PR. Emergency escape hatch is in the runbook.

### Day 44 - Documentation Skeleton

**Decision**: Architecture overview, C4 context and container diagrams (Mermaid), and three runbooks. "Service"
in the plan is read as "module" (ADR-0039). DEPLOY.md is honest that no hosting target exists. The
ADD-A-MODULE runbook was verified by adding a throwaway module, which was discovered with no `Program.cs` edit
and then removed. The ten reconcile markers were filled after Days 41 and 43; the two Day 42 ones were filled when Day 42 finished.

**Verification**: build, tests, `smoke-test.sh` and E2E 20/20 on the doc worker's own stack. Not verified:
`dotnet run` outside Docker, `contract-test.sh`, Mermaid rendering on GitHub.

### Integration of Days 41, 43 and 44

Merged into local `main` (`8ab60d7`) and re-verified on the merged tree: build 0 warnings, dependency scan 0
blocking, unit tests exit 0, integration tests 14 passed, `smoke-test.sh` exit 0, E2E 20/20. Also added
`.gitattributes` (`* text=auto eol=lf`) so `dotnet format --verify-no-changes` works in Windows worktrees.
Pilot note: the three workers each took roughly 1.5 to 3 hours of wall-clock; the pilot metrics report is not
written yet.

**Status** (as of the integration): complete and pushed; CI 11/11 and CodeQL green; branch protection enabled
afterwards (see the Day 43 update above).

### Day 42 - Performance Baseline

**Decision**: Settled four design questions with the user. The Gateway rate limit (hard-coded 100 per 10 s per IP)
is now configuration (`RateLimiting:PermitLimit` / `WindowSeconds`, `RateLimitingOptions`, validated at
start-up, defaults unchanged) so a one-machine load test is not answered with 429s. Load tool: k6 from Docker, a
`k6` service under the compose `perf` profile (never starts with a plain `up`), called from inside the compose
network. Scenarios: `health`, authenticated `read`, `write` (PostgreSQL + RabbitMQ), each at 1/10/25/50 virtual
users for 30 s. On demand via `build/scripts/perf-baseline.sh`, deliberately not a CI job (shared runners too
noisy; no targets exist to gate on).

**Result** (one run, one laptop under Docker Desktop, everything on one host; full table in
`docs/performance/BASELINE-2026-10-19.md`): zero failed requests and zero 429s at any level. At 50 users `health`
~1,100 rps (p95 90 ms), `read` ~540 rps (p95 170 ms), `write` ~180 rps (p95 ~800 ms, p99 ~2 s); `write` stops
scaling from about 10 users. Cause **not established**: the code opens and closes a RabbitMQ channel per publish
and RabbitMQ showed the highest CPU, recorded as an untested hypothesis. No performance targets exist yet.

**Bugs found while building it**: Git Bash rewrote the container path (`MSYS_NO_PATHCONV=1`); the first results
table divided by the whole test duration, so every rps was ~11x too low (caught by hand-checking, fixed before the
real run).

**Verification**: Gateway build 0 warnings; default limiter unchanged (131x200 / 19x429 on a 150 burst, matching
Day 22) and the configured value visible in the container; quick run exit 0; full run exit 0 with 12 scenarios and
a clean teardown. **Not verified**: run-to-run variation (single run), the write-path cause, rules-engine/login/Api
without Gateway paths.

**Status**: Complete on branch `day-42/performance-baseline`; goes to `main` by pull request (branch protection).
Days 41-44 are now all built. Next: the pilot metrics report, then Days 45-50, pending explicit instruction.

### Multi-agent pilot - metrics report (Days 41, 43, 44)

**Result**: `Phase-1_Architecture_Engineering_Prep/Multi_Agent_Pilot_Metrics_Report.md`. Active time was roughly
halved (about 80 min measured: 49 min concurrent window + about 3 min finishing + about 27 min integration, against
an **estimated** 2.5 to 3 h sequential), matching the 1.5x predicted, not the hoped-for 2x or more. Calendar time did
not improve (16 h 11 min, 14 h 50 min of it waiting for the human). Zero merge conflicts; the integrated tree passed
everything (CI 11/11 + CodeQL). No sequential baseline was measured and the foundation effort (module refactor,
slots, ADR-0035) is not counted. Workers' self-reported durations (1.5-3 h) were 2-4x the transcript durations
(about 49 min): verify from outputs, not worker narrative. Recommendation: use parallel workers selectively for
independent work over disjoint files; not for build chains or work needing an idle machine; do not scale to
per-bounded-context teams without a properly timed second comparison.

### Day 45 - Buffer / Catch-up (in progress)

Scope chosen by the user: four items, in order, each through its own PR: write-path investigation, low-risk Dependabot
batch, security items needing no new design, India tariff-sourcing research. Log: `Day45_Execution_Log.md`.

**1. Write-path investigation (ADR-0040)**: Tempo traces showed the RabbitMQ publish step was the cost of the
Day 42 write-path plateau (about 2.6 s of the slowest 2.7 s requests; 4.6 ms of a 10 ms request unloaded). The
publisher opened a channel, declared the exchange and closed the channel on every publish; `RabbitMqEventBus` now
reuses one lock-guarded channel. Alternating before/after runs: about 2x write throughput at 25 users and the
multi-second tail gone; recovery from a broker restart under load unchanged. Run-to-run variation on this machine is
about 25-30% (previously unknown). Also fixed: the perf script could fail its first login because `compose run` was
seen re-creating PostgreSQL and RabbitMQ (cause unknown; now `--no-deps` plus a login wait). Not established: why the
old pattern stalled for a uniform ~2.6 s.

**3. Security items needing no new design (ADR-0041)**: user took the recommendations. The Gateway's `/metrics` is
served only on an internal port (9100, never published) and is a 404 on the public port; the check is on the
connection's local port because the Host header is spoofable (spoofed-Host request verified 404). Prometheus scrapes
`gateway:9100`. Access-token lifetime is now `Jwt:AccessTokenMinutes` (default 60, was a hard-coded 8 h, validated
1-1440); no refresh flow yet. Login throttling was deliberately documented as an accepted gap, not built (needs design
with the identity provider). Security checklist items 1.6, 1.7, 7.5 updated.
