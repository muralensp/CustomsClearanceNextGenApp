# LEARNING_NOTES.md

A walkthrough of **BOSS NextGen** (Customs Clearance / Brokerage Platform, India) as it *actually
exists in the repository* — verified against source, csproj files, CI workflow, docker-compose,
scripts, tests, ADRs, and git history as of commit `bcc353c` (Day 18 complete, Day 19 design-only).

> **Living document.** Append to it as each day's work lands. Section 10 is the running changelog.
>
> **Companion:** `CUSTOMS_CLEARANCE_Tutorial.md` is the same system explained *top-down* — business
> purpose, the 17 bounded contexts, the 12-stage shipment lifecycle, and each flow marked
> ✅ built / 📗 finalized / 📙 proposed / ❌ undecided. This file is the bottom-up view (code,
> patterns, module internals). Read the Tutorial first if you want the "why"; read this one if you
> want the "how".

---

## 0. First, the most important framing

**There is no business logic in this codebase yet.** Not one customs entity, no Declaration class,
no Shipment class, no rules engine, no database migration. Days 1–19 built two things:

1. **Design artifacts** — 13 ADRs in `adr/`, ~25 `.docx` deliverables in
   `Phase-1_Architecture_Engineering_Prep/`, 2 OpenAPI specs in `openapi/`.
2. **A production-grade engineering chassis** — 7 Platform libraries, 8 test projects, a 6-job CI
   pipeline, containerization, and quality gates.

This is deliberate. Phase −1 is called "Architecture & Engineering Preparation." The bet is that
the cost of building the chassis *first* is repaid when 17 bounded contexts get built on top of it
in Phases 0–8, because none of them re-invents logging, errors, auth, audit, persistence, or
messaging. Understand this and every design choice below makes sense.

---

## 1. Architecture Overview

### 1.1 The intended target architecture

**Monorepo + Modular Monolith + Trunk-Based Development** (ADR-0003), organized as
**bounded-context-first Clean Architecture** (ADR-0006). 17 bounded contexts identified on Day 1
(16) + Day 4 (Manifest, #17). One deployable Api host today; contexts are modules inside it, each
with its own project pair, so any one can be extracted into a service later without rewriting it.

### 1.2 What physically exists right now

```
                        ┌───────────────────────────────────────────────┐
                        │        HTTP clients / Newman contract tests    │
                        └───────────────────────┬───────────────────────┘
                                                │ :8080
┌───────────────────────────────────────────────▼───────────────────────────────────────┐
│  CustomsClearance.Api   (src/Api — Minimal API, top-level Program.cs, net8.0)          │
│                                                                                        │
│  MIDDLEWARE PIPELINE  (order is load-bearing, see Program.cs:97-100)                    │
│    1. UseCustomsClearanceCorrelationId()  ← Platform.Logging                            │
│    2. UseCustomsClearanceErrorHandling()  ← Platform.ErrorHandling                      │
│    3. UseAuthentication()                 ← Platform.Security (JWT bearer)              │
│    4. UseAuthorization()                  ← Platform.Security (named policies)          │
│                                                                                        │
│  ENDPOINTS                                                                              │
│    GET  /health                                    ← always mapped (smoke test target)  │
│    GET  /diagnostics/throw-validation-error        ← Dev only                           │
│    GET  /diagnostics/secure    [RequireAdmin]      ← Dev only, writes an AuditEvent     │
│    POST /diagnostics/widgets                       ← Dev only + connstr guard           │
│    GET  /diagnostics/widgets/{id}                  ← Dev only + connstr guard           │
│                                                                                        │
│  src/Api/Diagnostics/  DiagnosticsWidget, DiagnosticsDbContext,                          │
│                        DiagnosticsWidgetRepository, DiagnosticsWidgetCreatedHandler      │
│                        (throwaway proof harness — NOT a bounded context)                 │
└───┬──────────┬──────────┬──────────┬───────────┬──────────┬───────────────────────────┘
    │          │          │          │           │          │
    ▼          ▼          ▼          ▼           ▼          ▼
┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌──────────┐ ┌──────────┐   ┌──────────┐
│Logging │ │ Error  │ │Security│ │ Audit  │ │Persistence│ │Messaging │   │  Common  │
│        │ │Handling│ │        │ │        │ │           │ │          │   │(placehldr)│
│Correl- │ │Problem │ │JwtOpts │ │Audit-  │ │Auditable- │ │DomainEvt │   │          │
│ationId │ │Details │ │Roles   │ │Event   │ │Entity<T>  │ │Envelope  │   │          │
│Middle- │ │Mapper  │ │Policies│ │IAudit- │ │IRepository│ │IEvent-   │   │          │
│ware +  │ │Middle- │ │AddAuth*│ │Writer  │ │EfRepo<T,K>│ │Publisher │   │          │
│Async-  │ │ware    │ │        │ │Console/│ │Platform-  │ │IEvent-   │   │          │
│Local   │ │Domain- │ │        │ │InMemory│ │DbContext  │ │Handler   │   │          │
│Accessor│ │Validat-│ │        │ │Writer  │ │           │ │InMemory- │   │          │
│        │ │ionExc. │ │        │ │        │ │           │ │EventBus  │   │          │
└───▲────┘ └───┬────┘ └────────┘ └───┬────┘ └─────┬─────┘ └────┬─────┘   └──────────┘
    │          │                     │            │            │
    └──────────┴─────────────────────┴────────────┼────────────┘
      (Logging is the shared leaf: ErrorHandling, │  Audit and Messaging all depend
       on it for CorrelationIdAccessor)           │
                                                  ▼
                                        ┌──────────────────┐
                                        │  PostgreSQL 16   │
                                        │  (docker-compose,│
                                        │  healthchecked)  │
                                        └──────────────────┘

CI  (.github/workflows/ci.yml — 6 jobs, all delegating to build/scripts/*.sh)
  build ──┬── lint                 (dotnet format --verify-no-changes)
          ├── test                 (tests/Unit/**, coverlet thresholds = hard gate)
          ├── integration-test     (tests/Integration/**, WebApplicationFactory, NO database)
          ├── containerize         (docker compose up + /health + real Postgres round-trip)
          └── contract-test        (Newman/Postman over real HTTP against docker-compose)
```

### 1.3 The dependency rule

`Platform.Logging` is the leaf everything else sits on — because `CorrelationIdAccessor` is the
single thread tying an HTTP request, its logs, its audit records, and its domain events together.
`ErrorHandling`, `Audit`, and `Messaging` each declare a `ProjectReference` to it. Nothing depends
on `Api`. That direction is never violated, and it's what makes the Platform libraries reusable by
future bounded contexts.

---

## 2. The 10 Most Important Engineering Decisions (and why, over what)

### D1 — .NET 8 / C# 12 / PostgreSQL (ADR-0001)
**Chosen over** Java (SAP GTS, Oracle GTM lineage) and Node.
**Why:** CargoWise — the market-leading customs brokerage platform — is .NET/C#. Hiring, ecosystem
familiarity, and domain-expert overlap all point the same way. PostgreSQL is primary, but *SQL
Server must stay reachable*: hence the standing rule "no provider-specific SQL, no stored procs."
You can see this honored — every data access in the repo goes through EF Core LINQ
(`EfRepository.cs`), never raw SQL.

### D2 — Configuration-driven rules engine, not code-driven (ADR-0002, ADR-0013)
**Chosen over** hardcoded `if` logic and over a COTS engine (Drools/NRules).
**Why:** India's de minimis threshold has *no single universal value* — it varies by import channel,
purpose, importer type, and commodity, and it changes by government notification. Encoding that in
C# means a release per policy change. So rules live in `Rule / RuleVersion / RuleCondition /
DecisionTrace`, versioned and effective-dated, editable by a compliance admin. This one decision
drives the entire data model (§4) and the whole of Phase 0 Days 31–35.

### D3 — Domain and Application layers merged (ADR-0006)
**Chosen over** textbook Clean Architecture's four separate projects.
**Why:** A two-person team with 17 contexts × 4 projects = 68 projects is administrative overhead
without payoff. Each context gets exactly two: `Context.csproj` (domain + application) and
`Context.Infrastructure.csproj`. Explicitly documented as an *adaptation*, not an accident — which
is the point of writing it down.

### D4 — Generic `IRepository<TEntity,TId>` in Platform, narrow interfaces per context (ADR-0006/0012)
**Chosen over** either a single god-repository or every context hand-rolling CRUD.
**Why:** The generic type is a *building block*, not the contract application code uses. A context
declares `IDeclarationRepository` with its own domain-meaningful methods and implements it on top
of `EfRepository`. You get shared plumbing without leaking `IQueryable` into the domain.

### D5 — Repository does NOT save; Unit of Work stays with the caller (ADR-0012)
**Chosen over** the common "save inside every repository method" shortcut.
**Why:** A use case that writes two aggregates must commit them atomically. `EfRepository.AddAsync`
only calls `DbSet.AddAsync`; the Application layer calls `SaveChangesAsync` once per use case. See
`EfRepository.cs:209-214` — the doc comment names the anti-pattern it is avoiding. `Program.cs:157`
shows the intended call shape: `repository.AddAsync(...)` then `dbContext.SaveChangesAsync()`.

### D6 — Placeholder implementations behind real ports (ADR-0011/0012)
`ConsoleAuditWriter` behind `IAuditWriter`; `InMemoryEventBus` behind `IEventPublisher`.
**Chosen over** waiting for the real infrastructure decision before building anything.
**Why:** The transport (Kafka vs RabbitMQ vs Azure Service Bus) and the audit sink (append-only
Postgres table) are Phase 0 decisions that depend on a hosting choice not yet made. But the
*contract* — the envelope shape, the handler signature — can be settled now and proven working.
Swapping later means one new DI registration in `AddCustomsClearanceMessaging()`; zero application
code changes. This is the Strategy pattern used as a scheduling tool.

### D7 — CI logic in portable shell scripts, workflow file kept thin (ADR-0005)
**Chosen over** putting `dotnet test ...` inline in GitHub Actions YAML.
**Why:** The user may migrate to Azure DevOps. `build/azure-pipelines.yml` is parked in lockstep.
Because all real logic is in `build/scripts/*.sh`, migration changes only trigger/orchestration
syntax — not a single build step. Portability bought cheaply, up front.

### D8 — Two coverage tiers (95% core, 80% CRUD), enforced per-project (ADR-0007)
**Chosen over** a blanket 95% (the original rule, corrected in v2) and over "report only."
**Why:** Demanding 95% on a CRUD master-data service produces test theater. Thresholds are declared
*in each test csproj* (`<Threshold>80</Threshold>`), so `dotnet test` itself fails the build — a
real gate, not a dashboard. Two hard-won details in there: `coverlet.msbuild` **not**
`coverlet.collector` (they conflict), and `<Include>[AssemblyName]*</Include>` to stop
transitive-dependency coverage bleed inflating the number.

### D9 — Four isolated test layers (ADR-0009)
Unit / Integration / Contract / Container-smoke, each its own CI job and its own script.
**Why:** If a Docker-dependent test lives in the same suite as unit tests, one flaky container
poisons the fast feedback loop. So: integration tests are **forbidden from touching
infrastructure** (in-process `WebApplicationFactory` only, 6 tests, 59ms), and the *only* layer
that touches real Postgres is the container smoke test. `TestApiFactory.cs` exists entirely to
enforce that boundary.

### D10 — "Nothing is done until proven against the real running system"
Not an ADR — a working agreement in `CLAUDE.md`, and the single highest-value rule here.
**Evidence it works:** Day 18 alone found **five real bugs** that unit tests had passed clean —
Scoped-handler-from-Singleton DI failure, a Postgres startup race, a masked CI failure hidden by a
pipe, an env-var override that silently no-op'd twice, and a Minimal API route-mapping crash that
took down the whole app. Every one surfaced only by running the thing for real. See §7.

---

## 3. Design Patterns in the Codebase (with file references)

| Pattern | Where | Notes |
|---|---|---|
| **Repository** | `src/Platform/Persistence/IRepository.cs`, `EfRepository.cs` | Generic base; contexts define narrower interfaces on top |
| **Unit of Work** | `PlatformDbContext` (the `DbContext` *is* the UoW) | Deliberately not folded into the repository — see D5 |
| **Template Method** | `PlatformDbContext.SaveChangesAsync` override | Stamps `CreatedAt`/`UpdatedAt`, then calls `base.SaveChangesAsync` |
| **Marker Interface** | `IAuditableEntityMarker` (`PlatformDbContext.cs:327`) | Non-generic marker so the change tracker can detect `AuditableEntity<T>` for *any* `T` without reflecting over open generics — a genuinely clever, cheap trick |
| **Publish/Subscribe (Observer)** | `IEventPublisher`, `IEventHandler<T>`, `InMemoryEventBus` | Multiple handlers per payload type; all invoked |
| **Strategy / Port-Adapter** | `IAuditWriter` → Console/InMemory; `IEventPublisher` → InMemory | The seam for swapping in real infrastructure |
| **Chain of Responsibility** | ASP.NET Core middleware: `CorrelationIdMiddleware` → `ExceptionHandlingMiddleware` → auth | Order is documented as load-bearing in `Program.cs:89-96` |
| **Factory Method** | `AuditEvent.Create(...)`, `DomainEventEnvelope<T>.Create(...)` | Both auto-stamp Id, UTC timestamp, and correlation ID so callers can't forget |
| **Ambient Context** | `CorrelationIdAccessor` using `AsyncLocal<string?>` | Flows through `await` continuations without threading a parameter through every signature |
| **Envelope / Message wrapper** | `DomainEventEnvelope<TPayload>` | EventId, EventType, EventVersion, OccurredAt, CorrelationId, Source, Payload |
| **Extension-method DI module** | `AddCustomsClearance*()` / `UseCustomsClearance*()` in every library | Each library owns its own registration; `Program.cs` stays declarative |
| **Null Object / Test Double** | `InMemoryAuditWriter` | Lets tests assert an audit event was actually raised |
| **Specification (designed, not built)** | ADR-0013's `IConditionEvaluator` | `RuleCondition` rows are runtime-composed specifications |

**Explicitly *not* used:** CQRS, MediatR, Event Sourcing. There's no in-process message bus
between application layers — handlers are resolved straight from DI. Given the team size, that's
proportionate; adding MediatR later is an unforced option, not a prerequisite.

---

## 4. Data Model

Two distinct models coexist here. Don't confuse them.

### 4.1 The business ER model — designed, signed off, **not yet coded**
From `Day4_ER_Diagram_v1.0.docx` (v1.0, signed off; v1.1 in the multimodal addendum):

```
      Manifest (#17, added Day 4)
      ManifestId, ManifestType(IGM|EGM|CourierManifest), ManifestNumber,
      ManifestLineNumber, FilingParty→Party, FiledAt, Status
                 │ 1..1
                 ▼
  Party ◄──ShipmentParty──►  Shipment | CourierShipment
  GSTIN/IEC/PAN               TransportMode, ImportPurpose,
  (≥1 required, waived for    ImporterType, OriginCountry,
   gift + below threshold)    DestinationCountry
                                        │ 1..N
                                        ▼
                                   Commodity  (ITC-HS code, ControlledGoodsFlag)
                                        │ N
                                        ▼
                          DeclarationCommodityLine
                          (snapshot-at-version: pins CommodityVersion)
                                        │ N..1
                                        ▼
                                  Declaration
                    DeclarationType (incl. CourierBillOfEntry/ShippingBill)
                    ManifestId + ManifestLineNumber
                    CT / CUS / RAT / MAN  (MAN now derived, not free-standing)
                    │              │                    │
          ┌─────────┘              │                    └──────────┐
          ▼                        ▼                               ▼
       Charge                   Permit                    ShipmentIndicator
    (BCD, IGST, Cess)   (ApprovalBlocksSubmission)
          └────────────┬───────────┴────────────────────────────┘
                       ▼
                     Rule  (Category, Priority 1..9)
                       │
                       ▼
                  RuleVersion  (EffectiveFrom / EffectiveTo, AND-only conditions)
                    │                          │
                    ▼                          ▼
             RuleCondition              DecisionTrace
      (FactName, Operator, Value)   (explainability record)
```

**Why the schema looks like this — the four decisions that shaped it:**

1. **Snapshot-at-version (`DeclarationCommodityLine` pins `CommodityVersion`).** Charges must be
   reproducible for audit years later, even if the Commodity master record is amended. A live FK
   would silently change history. Confirmed by Scenario A Step 7 in the Day 4 walkthrough.
2. **Courier reuses `Declaration` with a `DeclarationType` discriminator** rather than getting its
   own entity — the ShipmentId-or-CourierShipmentId FK pattern already existed, so only the enum
   needed extending. Less structure, same expressiveness.
3. **Party identity is conditional, not mandatory.** At least one of {GSTIN, PAN} is required for
   Import/Export/Broker roles — *except* gift imports below threshold, where both may genuinely be
   absent. Real Indian regulation, not an edge case to be normalized away.
4. **`RuleCondition` is AND-only.** OR is authored as multiple Rules at the same Priority in the
   same Category, since the engine already evaluates all matching rules per priority level. A
   modeling technique substituting for a schema feature — cheaper, and revisitable in Phase 3.

**Also note the Manifest finding.** Both Day 4 scenario walkthroughs hit the same wall: in India a
Bill of Entry cannot be lodged without referencing a carrier-filed manifest. The PRD treated this
as a status flag. Walking real scenarios through the model turned a flag into bounded context #17.
That's what scenario revalidation is *for*.

> ⚠️ **Open inconsistency worth resolving before Phase 0 Day 31.** `Day4_ER_Diagram_v1.0.docx` §4.5
> decided **one `DecisionTrace` row per pipeline run**, with an embedded JSON step array —
> explicitly justified by write volume at 500,000 shipments/day. But `adr/0013` §5 designs
> `IDecisionTraceRecorder` to emit **one row per pipeline stage** (`StepOrder` = stage index).
> These contradict. ADR-0013 is `Status: Proposed`, so this is a live question, not a bug — but it
> should be settled deliberately rather than discovered during the Day 31 build.

### 4.2 The persistence model — actually coded
The only real table today is `Widgets`, created by `EnsureCreatedAsync()` for smoke-testing:

```csharp
public abstract class AuditableEntity<TId> : IAuditableEntityMarker
{
    public required TId Id { get; init; }
    public DateTimeOffset  CreatedAt { get; internal set; }   // stamped by PlatformDbContext
    public DateTimeOffset? UpdatedAt { get; internal set; }   // never set manually
}
```

`internal set` is intentional: only `PlatformDbContext` (same assembly) may write these, so no
context can spoof its own timestamps. Note this is **row-level bookkeeping**, deliberately separate
from the legal audit trail (`Platform.Audit`'s `AuditEvent`). Two different concerns, two different
mechanisms — the doc comment on `AuditableEntity` says so explicitly.

**There are no EF Core migrations yet.** `EnsureCreatedAsync()` is dev-only scaffolding, called
under `IsDevelopment()` with retry/backoff. A real migration pipeline is Phase 0/3 work once real
entities exist.

---

## 5. Module Breakdown

### `Platform.Logging` — the foundation everything else stands on
- `CorrelationIdAccessor` — `static AsyncLocal<string?>`. Ambient, flows across `await`.
- `CorrelationIdMiddleware` — reads `X-Correlation-Id` or generates one, sets the accessor, echoes
  it on the response, and pushes it into `ILogger.BeginScope` so *every* log line in the request
  carries it automatically.
- **Talks to:** nothing (leaf). **Talked to by:** ErrorHandling, Audit, Messaging.
- Why `FrameworkReference Include="Microsoft.AspNetCore.App"` and not a NuGet package: it's the
  correct way for a plain class library to use ASP.NET Core types without becoming a Web SDK project.

### `Platform.ErrorHandling` — one error shape for the whole platform
- `DomainValidationException` — the *single* exception type business logic throws, carrying one or
  more `ValidationFailure(Field, Code, Message)` where `Code` is a PRD Business Rule ID (`BR-DEC-004`).
- `ErrorMapper` — **pure** `Exception → ProblemDetailsResponse`. Split out from the middleware
  specifically so it's unit-testable without an HTTP pipeline.
- `ExceptionHandlingMiddleware` — catches, maps, serializes as `application/problem+json`.
  `DomainValidationException` → 422 + `LogWarning` (expected). Anything else → 500, **`Detail: null`**,
  `LogError` server-side only. Internals never cross the wire.
- **Depends on:** Logging (for the correlation ID on every error response).

### `Platform.Security` — auth without an identity service
- `PlatformRoles` (Broker/Supervisor/ComplianceOfficer/Admin — matching the Day 1 Permissions
  Matrix exactly) and `PlatformPolicies` (four named policies). Endpoints reference *policy names*,
  never role string literals, so role membership can change in one place.
- JWT bearer with **symmetric key** validation today; `JwtOptions`' doc comment lays out the
  migration to `Authority`-based OIDC discovery once the Identity service exists (Phase 0 Day 23) —
  and correctly notes it's a config change, since `[Authorize]`, roles, and policies are unaffected.
- Fails fast: missing `Jwt:Key` throws at startup with a message pointing at the ADR.

### `Platform.Audit` — the legal trail
- `AuditEvent` record + `AuditEvent.Create()` factory that auto-stamps id/UTC/correlation ID and
  **validates attribution** — throws if neither `performedBy` nor `performedBySystem` is supplied.
  Every audit event must be attributable to someone or something.
- `AuditEventCategory` (Business vs Technical) — one model with a discriminator, not two systems.
- `DataClassification` (Public/Internal/Confidential/HighlyConfidential) drives *display-time*
  masking. `FieldChange` stores true values as-is: masking is a display concern, because the audit
  record must retain the real value for its own legal purpose.
- `IAuditWriter` → `ConsoleAuditWriter` (default) / `InMemoryAuditWriter` (tests).

### `Platform.Persistence` — see §4.2 and D4/D5
`AuditableEntity<TId>`, `IAuditableEntityMarker`, `IRepository<TEntity,TId>`, `EfRepository<,>`,
`PlatformDbContext`. **This is the only place EF Core/Npgsql types are meant to appear** —
Domain/Application code sees `IRepository`, never `DbContext`.

### `Platform.Messaging` — pub/sub with a swap seam
`DomainEventEnvelope<TPayload>` (the Day 6 API Standards v1.1 §3.1 shape, with `EventVersion`
following the same major/minor discipline as the REST APIs), `IEventPublisher`, `IEventHandler<T>`,
`InMemoryEventBus`. The bus is a **Singleton** that creates a **scope per publish** via
`IServiceScopeFactory` — see §7 bug #1 for why that isn't optional.

### `Platform.Common`
Empty. `PlaceholderModuleMarker` exists solely so CI has a real project to build/lint/test — and
`Describe()` is a method rather than only a const specifically so coverage measures a real line
instead of reporting a vacuous 0-of-0 "100%."

### `src/Api/Diagnostics/` — the proof harness
`DiagnosticsWidget`, `DiagnosticsDbContext`, `DiagnosticsWidgetRepository`,
`DiagnosticsWidgetCreatedHandler`. Throwaway. Its whole job is to make `POST /diagnostics/widgets`
exercise Persistence (real Postgres write) *and* Messaging (publish → handler logs) in one call, so
CI's smoke test can prove both libraries work against the real thing. Delete when a real context
exercises them naturally.

---

## 6. Infrastructure & Delivery

- **`Directory.Build.props`** — solution-wide `Nullable`, `ImplicitUsings`,
  `EnforceCodeStyleInBuild`, and `SonarAnalyzer.CSharp` as a Roslyn analyzer. Key insight: this
  gives Sonar's C# rule set at build time **with no SonarCloud/SonarQube server**. A dashboard is a
  later optional upgrade; the analysis itself isn't blocked on it.
- **`src/Api/Dockerfile`** — multi-stage (SDK builds, `aspnet` runtime runs). csproj files are
  copied *before* sources so `dotnet restore` layer-caches. Build context is the repo root, since
  Api references sibling Platform projects. **Maintenance cost:** every new Platform library needs
  two lines added here — easy to forget.
- **`docker-compose.yml`** — Api + Postgres 16-alpine, with a `pg_isready` healthcheck and
  `depends_on: condition: service_healthy`. Note `Host=postgres` (the compose DNS name) in the
  compose env override vs `Host=localhost` in `appsettings.Development.json` for `dotnet run`.
- **CI (6 jobs)** — `build` → then `lint`, `test`, `integration-test`, `containerize`,
  `contract-test` in parallel. Every job is `bash build/scripts/<x>.sh`.
- **`test.sh`** carries a critical warning: never pass `--no-build`. `coverlet.msbuild` hooks
  instrumentation into `dotnet test`'s own build; reusing prior output silently yields an empty
  coverage report instead of failing loudly.

---

## 7. The Five Day-18 Bugs — the most instructive material in the repo

Each was found by running the real system. Each is now a permanent lesson in `CLAUDE.md`.

1. **Singleton resolving a Scoped handler.** `InMemoryEventBus` is a Singleton; `IEventHandler<T>`
   implementations are typically Scoped (they hold a `DbContext`). Resolving Scoped from the root
   provider throws. **Fix:** inject `IServiceScopeFactory`, create a scope per `PublishAsync`. This
   is the *correct* design for a message bus, not a workaround — a real broker does the same thing
   per delivered message. (Nicely, the exception was caught cleanly by the Day 16 error middleware
   as a 500 with nothing leaked — the chassis proving itself while being debugged.)
2. **`depends_on` isn't readiness.** List-form `depends_on` waits for container *start*. Postgres's
   first-time init includes an internal restart, so the Api connected too early and the unhandled
   failure killed the whole process. **Fix:** healthcheck + `condition: service_healthy`, *plus*
   a 5-attempt retry loop in `Program.cs` as defense in depth. Two layers, because the orchestration
   fix doesn't cover a `dotnet run` against an already-up-but-busy database.
3. **A green CI run that was actually a failure.** The command was piped through `tail`, so the
   pipeline saw `tail`'s exit code, not the script's. **Rule:** never trust a piped exit code.
4. **An env-var override that silently no-op'd — twice.** First attempt used
   `ConfigureAppConfiguration`, which runs *after* minimal-hosting `Program.cs` has already read
   config while building the `WebApplicationBuilder`. Second attempt set the var to `""` — and on
   Windows, `SetEnvironmentVariable(name, "")` **deletes** the variable. **Fix:** a single-space
   placeholder, set in `TestApiFactory`'s static constructor before any host exists.
5. **One bad endpoint parameter crashed every endpoint.** Minimal API's `RequestDelegateFactory`
   validates parameter bindability at **route-map time**, not per request. An unregistered
   `DbContext` parameter on `/diagnostics/widgets` took down `/health` too. **Rule:** guard the
   *route mapping* with the same condition that guards the service registration — not just the
   handler body.

The through-line: **every one of these passed unit testing.** That's the empirical case for
working agreement D10.

---

## 8. What You Should Study

**Tier 1 — needed to read this codebase today**
- ASP.NET Core middleware pipeline & ordering; `IApplicationBuilder`, `RequestDelegate`
- DI lifetimes: Singleton vs Scoped vs Transient, and the **captive dependency** problem (bug #1)
- Minimal APIs and `RequestDelegateFactory`'s map-time validation semantics (bug #5)
- `AsyncLocal<T>` and execution-context flow across `await`
- EF Core: `DbContext` as Unit of Work, `ChangeTracker` + `EntityState`, `DbSet.FindAsync`
- C# 12: records, `required`, `init`, primary constructors, nullable reference types
- RFC 7807 Problem Details; JWT structure, claims, and validation parameters
- xUnit + `WebApplicationFactory<T>`; coverlet's msbuild integration

**Tier 2 — needed to build the next phases well**
- DDD: bounded contexts, aggregates, domain events, context mapping (Evans; Vernon)
- Clean/Hexagonal Architecture — and specifically *why* this repo departs from it (ADR-0006)
- Repository + Unit of Work as a *pair*, and the failure mode of merging them
- Rules-engine design: forward chaining, conflict resolution, effective-dating, temporal data
  (Fowler's "Rules Engine" and Snodgrass on temporal databases)
- EF Core migrations, concurrency tokens, and the still-unwritten migration strategy here
- Message brokers: at-least-once delivery, idempotency, outbox pattern, dead-letter queues —
  all of which `InMemoryEventBus` currently *doesn't* do, and which the swap will force you to face

**Tier 3 — domain knowledge, and honestly the hardest part**
- Indian customs: ICEGATE, Bill of Entry, IGM/EGM, ITC-HS classification, BCD/IGST/Cess,
  de minimis by channel/purpose/importer type, DGFT licensing, courier vs cargo regimes
- DPDPA 2023 obligations (the gap ADR-0004 closed — it was not in the PRD)
- Read `Phase-1_Architecture_Engineering_Prep/Business_Process_Details.md` and the PRD `.docx`

**Suggested reading order for the code itself:**
`CLAUDE.md` → `adr/0001` → `adr/0006` → `src/Platform/Logging/` → `src/Platform/ErrorHandling/` →
`src/Api/Program.cs` → `src/Platform/Persistence/` → `src/Platform/Messaging/` → `adr/0012` (the
bug narratives) → `adr/0013` (where it's all heading).

---

## 9. Honest Assessment — Gaps and Risks

Not criticism; a map of what's still open, all of it named deliberately in the ADRs rather than
overlooked.

| Gap | Status | When it bites |
|---|---|---|
| Zero business logic / zero real entities | By design (Phase −1) | Phase 0 |
| No EF Core migrations (`EnsureCreatedAsync` only) | Deferred to Phase 0/3 | First schema change against real data |
| No hosting/cloud target chosen | Deferred to Phase 0 Day 21 | Blocks real CD, secrets management, broker choice |
| No frontend at all | Later phase | `Gate4_Frontend_Review.md` is an empty checklist for now |
| `InMemoryEventBus` has no durability/retry/DLQ | Named placeholder | Any real cross-process event |
| `ConsoleAuditWriter` isn't an append-only store | Named placeholder | Any real compliance audit |
| Symmetric JWT key, no Identity service | Until Phase 0 Day 23 | Production auth |
| DecisionTrace granularity contradiction (§4.1) | **Unresolved** | Phase 0 Day 31 — resolve first |
| Dockerfile needs manual edits per new Platform lib | Live maintenance cost | Next library added |
| `Platform.Common` is empty | Intentional CI scaffold | — |

---

## 10. Changelog

| Date | Change |
|---|---|
| 2026-08-13 | Added `CUSTOMS_CLEARANCE_Tutorial.md` (top-down system mental model) and cross-linked it here. New facts surfaced while writing it, worth knowing from this side too: the PRD targets **500,000 shipments/day** (Ch.1.3) with segregation < 5s; **no background job scheduler has been identified anywhere**, despite Ch.3 Stage 11's retry policy and ICEGATE polling needing one; and the undecided hosting target (ADR-0008) is currently blocking four downstream decisions — secrets management, event broker, observability stack, deployment topology. |
| 2026-08-13 | Initial version. Verified against commit `bcc353c` — 7 Platform libraries, 8 test projects, 6 CI jobs, 13 ADRs, Day 18 complete, Day 19 design-only. Flagged the DecisionTrace granularity contradiction between ER v1.0 §4.5 and ADR-0013 §5. |
