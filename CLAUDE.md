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

**Verification**: build (exit 0, 0 warnings), lint (exit 0), unit tests (exit 0, 7 projects, 96.5%
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
first live run. Fixed with `RabbitMqConnectionHelper.CreateConnectionWithRetry` (10 attempts, 2s
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
