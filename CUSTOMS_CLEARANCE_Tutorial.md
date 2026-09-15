# CUSTOMS_CLEARANCE_Tutorial.md
## A System Mental Model for BOSS NextGen — Customs Clearance Platform (India MVP)

Top-down conceptual architecture. Every claim is traced to a source: PRD chapter, ADR, day
deliverable, or an actual file in the repo.

---

## ⚠️ Read This Legend First — it is the whole point of the document

Almost nothing in this system is built yet. What exists is a **decided design** plus an
**engineering chassis**. So every single item below carries a status marker:

| Marker | Meaning | How to verify |
|---|---|---|
| **✅ BUILT** | Code exists and runs. Proven against the real system. | Open the file |
| **📗 FINALIZED** | Decided and signed off in writing. No code yet. Not expected to change. | Signed-off `.docx` or an ADR with `Status: Accepted` |
| **📙 PROPOSED** | Designed and written down, but explicitly still open to change | ADR with `Status: Proposed`, or a recommendation not yet ratified |
| **❌ UNDECIDED** | Named as a gap on purpose. Nobody has chosen yet. | Called out in an ADR's "deferred" section |

If you remember one thing: **the design is far ahead of the code, and that is deliberate.**
Phase −1 exists to decide everything that is expensive to change later. Day 19 of 50 is complete.

---

# 1. What This Platform Is Trying to Accomplish

**In one sentence:** BOSS NextGen automates the end-to-end customs clearance lifecycle so a customs
broker can move a shipment from "arrived from an upstream logistics system" to "cleared and released
by customs" with minimal manual touch — and can *prove* afterwards exactly why every decision was
made.

### The real-world problem

A customs broker in India sits between three parties who do not talk to each other:

```
  Freight forwarder / logistics system        Government (ICEGATE / CBIC / DGFT)
                │                                          ▲
                │  "here's a shipment"                     │  "here's a legal declaration"
                ▼                                          │
        ┌───────────────────────────────────────────────────────┐
        │   THE BROKER  — must answer, per shipment:            │
        │   • Does this even need formal clearance?             │
        │   • What is this good, in ITC-HS terms?               │
        │   • What duty, IGST, and Cess are owed?               │
        │   • Does it need a permit or DGFT licence?            │
        │   • Is it restricted, prohibited, or sanctioned?      │
        │   • Is the paperwork legally complete?                │
        └───────────────────────────────────────────────────────┘
                                │
                                ▼
                          Importer / Exporter
```

Today much of that is human judgment applied at volume. The PRD targets **500,000 shipments/day**
(Ch.1.3). Human judgment does not scale to that number. But it cannot simply be replaced with code,
because of a hard constraint discussed next.

### The two goals that shape every technical decision

**Goal 1 — Automate the decision.** SLA targets from PRD Ch.3.5: segregation < 5s, duty calculation
< 5s, validation < 10s, declaration creation < 15s.

**Goal 2 — Explain the decision.** This is the harder one and it is *not* secondary. Customs is a
legal process. When an authority audits a two-year-old declaration, the platform must reproduce:
which rule fired, what version of that rule, what facts it saw, and what value the tariff table held
*on that date*. That is why `DecisionTrace` is a first-class entity, why `DeclarationCommodityLine`
snapshots `CommodityVersion` instead of holding a live FK, and why `AuditEvent.Create()` refuses to
build an event that isn't attributable to a user or a system.

> **Automate the decision, and never lose the ability to explain it.** Every design choice below is
> downstream of those two sentences.

### The constraint that shaped the architecture more than any other

**📗 FINALIZED — Day 1 §4.4, binding design constraint:** *De minimis must never be hardcoded.*

India has no single de minimis threshold. Treatment depends on import channel (Courier / Cargo /
Postal), import purpose (Commercial / Personal / Gift), importer type (Individual / Business),
commodity, and whichever CBIC exemption notification is currently in force — and any of those can
change by government notification, with no software release.

The implication is stated bluntly in the source document: **value alone must never determine the
processing path.** That single sentence forced the configuration-driven rules engine (ADR-0002),
which forced effective-dated rule storage, which forced the `Rule / RuleVersion / RuleCondition /
DecisionTrace` schema, which is now the spine of the entire data model.

### Scope for the India MVP

**In (📗 FINALIZED, Day 1 §1.1):** Import/Export/Domestic/Cross-border via Air, Ocean, Road ·
Courier as a first-class channel · segregation · commodity + ITC-HS classification · declarations
incl. amendment/cancellation/resubmission/assessment tracking · duty/GST/Cess · permits + DGFT ·
compliance screening · ICEGATE submission · full administration (users, roles, calendars, feature
flags, config versioning).

**Out / deferred:** FTZ & bonded workflows (SEZ complexity) · Rail · multi-country tariffs (core
stays country-neutral, only India rules authored) · Warehouse/Fleet/TMS/ERP/HR/CRM/Accounting —
**integrate by API, never build in-platform.**

---

# 2. Major Business Domains (Bounded Contexts)

**📗 FINALIZED — 17 contexts**, 16 from Day 1 v2 plus Manifest added on Day 4. **✅ BUILT: zero.**
Not one of these exists as code yet.

```
                     ┌──────────────────────────────┐
                     │ 12. User / Org Administration │  ← auth for everything (sync)
                     └───────────────┬──────────────┘
                                     │
  ┌────────────────┐    ┌────────────▼──────────┐    ┌──────────────────────────┐
  │ 9. Party /     │───►│ 1. Shipment Management │───►│ 2. SEGREGATION ENGINE    │
  │   Customer Mgmt│    │    (mode-agnostic)     │    │  Decides Clearance Type. │
  └────────────────┘    │  sub: Journey, Leg,    │    │  THE PIVOT POINT of the  │
                        │  Tracking, Milestone,  │    │  whole platform.         │
  ┌────────────────┐    │  17. Manifest-Assoc.   │    └────────────┬─────────────┘
  │16. Courier     │───►└────────────┬───────────┘                 │
  │   Shipment Mgmt│                 │                             │
  │  (first-class, │                 ▼                             │
  │   NOT a subtype)│      ┌────────────────────┐    ┌─────────────▼──────────┐
  └────────────────┘      │ 3. Commodity Mgmt  │───►│ 4. HS Classification    │
                          └─────────┬──────────┘    │    Engine (ITC-HS)      │
                                    │               └─────────────┬───────────┘
                          ┌─────────▼──────────┐    ┌─────────────▼───────────┐
                          │ 6. Duty & Tax Engine│◄──►│ 7. Permit Mgmt + DGFT   │
                          │  (BCD, IGST, Cess) │    │   (co-deployed with #8) │
                          └─────────┬──────────┘    └─────────────┬───────────┘
                                    │                             │
                          ┌─────────▼─────────────────────────────▼───────────┐
                          │ 8. Compliance Engine (restricted/prohibited/       │
                          │    sanctions/end-use/regulatory holds)             │
                          └─────────────────────┬─────────────────────────────┘
                                                ▼
                          ┌──────────────────────────────────────────────────┐
                          │ 5. DECLARATION MANAGEMENT — the legal centerpiece │
                          │   draft │ version │ amend │ cancel │ resubmit     │
                          └───────────┬──────────────────────┬───────────────┘
                                      ▼                      ▼
                          ┌───────────────────┐   ┌────────────────────────────┐
                          │ 10. Workflow Engine│   │ 11. Customs Gateway        │
                          │  CT/RAT/CUS/MAN    │   │     Integration (ICEGATE)  │
                          └───────────────────┘   └────────────────────────────┘

  Later phases (not on the critical path):
  13. Document Mgmt (Ph.7) · 14. Financial/Charges billing (Ph.6) · 15. Reporting (Ph.9)
```

### Three context decisions worth understanding, because they teach the reasoning style

**Courier is context #16, not a Shipment subtype.** The tempting design is `Shipment.Channel =
Courier`. Rejected: Indian courier flows carry genuinely distinct simplified/consolidated
declaration rules and their own de minimis treatment. Forcing them into the cargo model means an
ever-growing thicket of `if (channel == Courier)` inside Shipment Management. A separate context
keeps the cargo model clean.

**Permit Management is #7 architecturally, but ships inside the same deployable unit as Compliance
Engine #8.** This separates *design boundary* from *deployment boundary* — and it is the modular
monolith idea in miniature. The boundary is real and must "remain separable later without
redesign"; the process count is an operational decision made independently.

**Manifest is #17, and it was discovered, not planned.** The PRD treated the manifest as a status
flag (`MAN`) on Declaration. Then Day 4 walked two real scenarios through the model and *both* hit
the same wall: in India a Bill of Entry cannot be lodged without referencing a carrier-filed IGM.
That's a real upstream entity, not a flag. So `Declaration` gained `ManifestId` +
`ManifestLineNumber`, and `MAN` became a *derived* status. **The lesson: walking concrete scenarios
through an abstract model finds gaps that reviewing the model alone never will.**

---

# 3. Major Technical Components

### Layer A — What actually exists today (✅ BUILT)

| Component | File | What it does |
|---|---|---|
| `CustomsClearance.Api` | `src/Api/Program.cs` | Single Minimal API host. `/health` + 4 dev-only diagnostics endpoints. No business logic. |
| `Platform.Logging` | `src/Platform/Logging/` | `CorrelationIdAccessor` (`AsyncLocal`) + middleware. The leaf everything depends on. |
| `Platform.ErrorHandling` | `src/Platform/ErrorHandling/` | `DomainValidationException` → RFC 7807 `ProblemDetailsResponse`. 422 for business rules, 500 with **no leaked detail** otherwise. |
| `Platform.Security` | `src/Platform/Security/` | JWT bearer + 4 roles + 4 named policies. |
| `Platform.Audit` | `src/Platform/Audit/` | `AuditEvent` + `IAuditWriter`. Business vs Technical category; 4-tier data classification. |
| `Platform.Persistence` | `src/Platform/Persistence/` | `AuditableEntity<TId>`, `IRepository<,>`, `EfRepository<,>`, `PlatformDbContext`. |
| `Platform.Messaging` | `src/Platform/Messaging/` | `DomainEventEnvelope<T>`, `IEventPublisher`, `IEventHandler<T>`, `InMemoryEventBus`. |
| `Platform.Common` | `src/Platform/Common/` | Empty. A CI scaffold. |
| PostgreSQL 16 | `docker-compose.yml` | Healthchecked. One table: `Widgets` (a test artifact). |
| CI/CD | `.github/workflows/ci.yml` | 6 jobs. Build, lint, unit+coverage gate, integration, containerize+smoke, contract. |

### Layer B — Designed, not built

| Component | Status | Source |
|---|---|---|
| Rules Engine (`IFactsProvider`, `IRuleSetLoader`, `IConditionEvaluator`, `IPriorityResolver`, `IDecisionTraceRecorder`, `RulesEngine`) | 📙 **PROPOSED** — ADR-0013 is `Status: Proposed` | Phase 0 Days 31–35 |
| 17 bounded-context modules | 📗 FINALIZED as boundaries; **❌ nothing designed internally** | Day 1 v2 |
| API Gateway | 📗 FINALIZED *role* (external ingress only); ❌ product unchosen | Day 6 v1.1 §2; Phase 0 Day 22 |
| Identity Service | 📙 PROPOSED — "implement **or** integrate a managed provider" | Phase 0 Day 23 |
| Event Bus (real broker) | ❌ **UNDECIDED** — Kafka *or* RabbitMQ *or* Azure Service Bus | Phase 0 Day 24 |
| Observability stack | 📙 PROPOSED with "or equivalent" everywhere — ELK/Loki, Prometheus, Grafana | Phase 0 Days 26–30 |
| Configuration Service, Notification Service | 📙 PROPOSED | Phase 0 Days 36–37 |
| Frontend | ❌ **UNDECIDED** — no framework, no design, nothing. `Gate4_Frontend_Review.md` is an empty checklist. | — |

---

# 4. How the Components Communicate

**📗 FINALIZED — Day 5 §1–2, Day 6 v1.1 §2.** This is one of the most thoroughly-decided areas.

### Rule 1 — Events by default, direct calls by exception

The default bias is **Event (async)**. Every synchronous call must be justified individually, and
all seven of them are, in Day 5's *Direct Call Justification Register* — so a future reviewer never
has to re-derive why a sync dependency was permitted.

```
EVENT (async)  — "your state changed; someone else may care, you don't wait"
  Shipment Management ──ShipmentValidated──►  Segregation Engine
  Segregation Engine  ──ClearanceTypeAssigned──►  Declaration Management
  HS Classification   ──CommodityClassified──►  Duty & Tax Engine + Permit Mgmt
  Duty & Tax Engine   ──ChargesCalculated──►  Declaration Management
  Customs Gateway     ──CustomsResponseReceived──►  Declaration Mgmt + Workflow Engine

DIRECT CALL (sync) — only 7 exist, each justified:
  1. Auth check (Admin → every context)    ...security boundary, sync by nature
  2. Shipment Mgmt → Journey/Leg/Manifest  ...same deployable unit; just a method call
  3. Commodity Mgmt → HS Classification    ...broker is typing; needs a code NOW
  4. Permit Mgmt → Declaration (submit gate)  ...BR-DEC-004: cannot submit on stale permit data
  5. Compliance → Declaration (submit gate)   ...final regulatory check must be current
  6. Manifest → Declaration (validation)      ...Indian law: no valid manifest, no lodgement
  7. Declaration → Customs Gateway            ...need the immediate transmission ack
```

**The pattern in exceptions 4, 5, 6 is the lesson.** Each is a *submission-time gate*. Eventual
consistency is fine right up until the moment you make a legally-binding filing to a government —
at that instant you must read current state, not a possibly-stale event projection. Note each of
those contexts publishes events *as well* (`PermitStatusChanged`, `ComplianceFlagRaised`,
`ManifestFiled`) for everyone else. **Sync at the legal boundary, async everywhere else.**

### Rule 2 — Transport depends on the deployment boundary

| Communication | Mechanism | Through the gateway? |
|---|---|---|
| UI → Backend | REST (GraphQL rejected for MVP) | ✅ Yes — real external ingress |
| External system → Platform | REST | ✅ Yes |
| Internal service → internal service | REST or gRPC, direct | ❌ No — never leaves the trust boundary |
| Same deployable module | In-process method call | ❌ No — not a network call at all |

The principle: *all inter-context communication uses published contracts; only the **transport**
varies with the deployment boundary.* This is what lets a module later become a service without a
contract rewrite. The gateway stays focused on ingress concerns — rate limiting, external auth,
public versioning.

### Rule 3 — One versioning philosophy for everything

REST uses `/api/v{n}/`. Everything else carries a version *field*: `EventVersion` (domain and
integration events alike), `CommandVersion` (reserved now so it needn't be retrofitted),
`WebhookVersion`, and `AdapterVersion` for ICEGATE messages — **deliberately decoupled from
ICEGATE's own external versioning, since the government's schema is outside our control.**

**✅ This one is BUILT** — `DomainEventEnvelope<TPayload>` in `src/Platform/Messaging/`:

```csharp
public sealed record DomainEventEnvelope<TPayload>(
    Guid EventId, string EventType, string EventVersion,
    DateTimeOffset OccurredAt, string? CorrelationId, string Source, TPayload Payload);
```

`Create()` auto-stamps `EventId`, UTC `OccurredAt`, and pulls `CorrelationId` from
`CorrelationIdAccessor.Current` — so a developer *cannot forget* to thread correlation through.
`AuditEvent.Create()` does the identical thing. **One correlation ID ties the REST call, every
event, every audit row, and the ICEGATE submission into a single traceable thread.**

---

# 5. Data Flow Through the System

**📗 FINALIZED design (PRD Ch.3.4, 12 stages) · ✅ BUILT: none of it.**

Here is the complete life of one shipment. This is the mental model to hold.

```
STAGE 1 ── SHIPMENT DOWNLOAD                                        SLA < 30s
  Upstream logistics system transmits MAWB/HAWB, parties, packages,
  commodity descriptions, invoice values, freight, insurance.
  → validate mandatory fields → create Shipment → store audit event
  → status = Downloaded
  📤 ShipmentCreated
  ✗ exceptions: duplicate · invalid customer · missing data · unsupported country

STAGE 2 ── SHIPMENT VALIDATION                                      SLA < 10s
  Customer exists? Country codes valid? Currency supported? Transport mode
  valid? Commodities present? Documents attached? Addresses present?
  📤 ShipmentValidated  ─or─  📤 ShipmentValidationFailed → broker task

STAGE 3 ── SEGREGATION ══ THE PIVOT POINT ══                        SLA < 5s
  The Segregation Engine reads ~13 factors: direction, origin/destination,
  value, customer profile, HS code, commodity type, indicators, customer
  warnings, postal codes, FTZ locations, controlled/dutiable/dangerous flags.
  It assigns a CLEARANCE TYPE (CT):
      Informal · Formal · FTZ · DOM · LND · DEM (de minimis) · ABK
  📤 ClearanceTypeAssigned (+ DecisionTraceId — the explainability handle)

        Brokerage required?
        ├── NO  → Release. Done.
        └── YES → continue ↓

STAGE 4 ── DECLARATION CREATION                                     SLA < 15s
  Auto-created (FR-DEC-001) from the CT. Parties, commodity lines, currency,
  customs office, declaration type, and four initial statuses: CT/RAT/CUS/MAN.
  📤 DeclarationCreated

STAGE 5 ── COMMODITY CLASSIFICATION  (per line, independently)
  Validate ITC-HS → retrieve tariff → derive duty rate, excise rate, permit
  requirement, OGA requirement, restrictions, statistical unit.
  📤 CommodityClassified → fans out to BOTH Duty & Tax AND Permit Mgmt

STAGE 6 ── DUTY & TAX CALCULATION                                   SLA < 5s
  Strict sequence: Customs Value → Freight Adj → Insurance Adj → Statistical
  Value → Import Duty (BCD) → Excise → GST/IGST → Govt Fees → Brokerage.
  ⚠ Every calculation logged with formula, rate, AND source table — for audit.
  📤 ChargesCalculated

STAGE 7 ── PERMIT VERIFICATION
  Evaluates HS code, description, origin, destination, agency rules.
  If required: 📤 PermitRequired → task created → SUBMISSION BLOCKED
              → status = Awaiting Permit

STAGE 8 ── COMPLIANCE VALIDATION                                    SLA < 10s
  Restricted · sanctioned parties · dual-use · dangerous goods · customer
  flags · document completeness · value thresholds · licensing.
  📤 ComplianceClearedForSubmission  ─or─  📤 ComplianceFlagRaised

STAGE 9 ── BROKER REVIEW  (only when needed)
  Triggered by: HS undeterminable · duty needs adjustment · permit missing ·
  docs incomplete · compliance exception · customs asked for clarification.
  Broker may edit declaration/commodities, upload docs, add remarks,
  request supervisor approval, resubmit.

  ══ THREE SYNCHRONOUS GATES fire here, in order ══
     Permit approved?  Compliance current?  Manifest reference valid?
     All three are Direct Calls — see §4, exceptions 4/5/6.

STAGE 10 ── CUSTOMS SUBMISSION                                      SLA < 30s
  Generate payload → validate format → digitally sign → transmit (API/EDI)
  → record SubmissionTimestamp → await ack.
  📤 DeclarationReadyForSubmission, then 📤 DeclarationSubmitted

STAGE 11 ── CUSTOMS RESPONSE PROCESSING                       real-time
  Accepted → clearance          Rejected → broker work item
  Inspection → hold + notify    Permit Required → await permit
  More docs → request           System Error → retry per configured policy
  📤 CustomsResponseReceived   (all responses recorded in event history)

STAGE 12 ── CLEARANCE & RELEASE
  Update clearance status → record release date → mark ready for delivery
  → notify downstream logistics → financial postings & reporting.
  📤 ClearanceGranted
```

### The loop that makes this hard: Re-Segregation

**📙 PROPOSED (PRD Ch.5.11, deferred by ADR-0013):** the flow above is not one-directional. If a
broker changes an HS code at Stage 9, or adds a commodity, the CT decided at Stage 3 may no longer
be correct. `ReSegregationTriggered` fires and stages 3→8 re-run.

This is precisely why the Day 5 catalogue is event-driven, and why `DecisionTrace` must be
*append-only* — you need every evaluation in history, not just the last one.

---

# 6. User Interaction Flow

**❌ MOSTLY UNDECIDED.** No frontend framework chosen. No wireframes. No component library. The
PRD describes screen layouts per module, but nothing has been designed or built. What *is* settled:
UI talks REST through the gateway; GraphQL is rejected for MVP.

**📗 FINALIZED — the four personas** (Day 1 Permissions Matrix, PRD Ch.4.9). ✅ These four **are
built**, in `src/Platform/Security/PlatformRoles.cs`:

| Role | Does what |
|---|---|
| **Broker** | Day-to-day: reviews exceptions, edits declarations, classifies commodities, submits |
| **Supervisor** | Approves overrides — notably the CT manual override (Ch.5.12), reason mandatory |
| **ComplianceOfficer** | Resolves regulatory issues, permits, holds |
| **Admin** | Users, roles, config, and **authoring the business rules** |

### The one UI flow you can already reason about concretely — a CT manual override

```
1. Broker opens a shipment; sees CT = Formal, disagrees.
2. UI shows the DecisionTrace: "Rule R-042 v3 fired — ControlledGoodsFlag = true"
        ▲ this is Goal 2 (explainability) surfacing directly in the UI
3. Broker requests an override → needs Supervisor role → escalation task created
4. Supervisor opens it. Reason is MANDATORY (PRD Ch.5.12).
5. On save:
     → an override DecisionTrace row is written ALONGSIDE, not instead of, the
       automated one (ADR-0013 — never destroy the machine's reasoning)
     → an AuditEvent is written (BusinessAudit, with FieldChange old→new)
     → 📤 ManualOverrideApplied published
     → Workflow Engine reacts; downstream stages re-run
```

Notice what that flow demands of the platform: role-based authorization ✅ built, an audit trail
with old/new values ✅ built, an event envelope ✅ built, decision traces 📙 designed, and the
screens ❌ undecided. That ratio is an honest summary of the whole project right now.

---

# 7. Backend / API Flow

**📗 FINALIZED standards · ✅ Cross-cutting middleware BUILT · ❌ Business endpoints: none.**

### What a request does today — verified, running code

```
HTTP request
  │
  ▼ [1] CorrelationIdMiddleware            ✅ Platform.Logging
  │      reads X-Correlation-Id, or generates one
  │      → CorrelationIdAccessor.Current = id     (AsyncLocal, flows across await)
  │      → echoes it on the response header
  │      → pushes it into ILogger.BeginScope  ⇒ EVERY log line in this request carries it
  │
  ▼ [2] ExceptionHandlingMiddleware        ✅ Platform.ErrorHandling
  │      try { next() }
  │      catch DomainValidationException → 422 + LogWarning  (expected failure)
  │      catch anything else             → 500, Detail:null, LogError (server-side only)
  │      Content-Type: application/problem+json
  │
  ▼ [3] UseAuthentication()                ✅ Platform.Security — validate JWT
  ▼ [4] UseAuthorization()                 ✅ Platform.Security — evaluate named policy
  │
  ▼ [5] Endpoint handler
```

**Order is load-bearing and documented as such** in `Program.cs:89-96`. Correlation ID *must* come
first so every error response and every log line carries it.

### The error contract — one shape for the whole platform

```json
{
  "type": "https://api.customsclearance.india/errors/validation-failed",
  "title": "Business rule validation failed",
  "status": 422,
  "detail": "...",
  "instance": "/api/v1/declarations/abc",
  "correlationId": "…",
  "errors": [ { "field": "hsCode", "code": "BR-DEC-004", "message": "…" } ]
}
```

RFC 7807, extended with `correlationId` and an `errors[]` array where **`code` is a PRD Business
Rule ID**. That link matters: a support engineer reads `BR-DEC-004` in a response and goes straight
to the PRD clause. It's traceability from a production error back to the requirement.

A key detail in `ErrorMapper.Map()`: for unknown exceptions `Detail` is **`null`**. Stack traces,
type names, and connection strings never cross the wire — they go to the server log only.

### The intended business flow (📙 for real endpoints, ✅ for the mechanics)

```
POST /api/v1/declarations/{id}/submit
  │
  ▼ API layer         parse, bind, authorize via named policy       ✅ mechanism built
  ▼ Application layer the USE CASE. Orchestrates:                   ❌ none written
  │     ├─ sync gate: Permit Mgmt — all permits Approved?     (BR-DEC-004)
  │     ├─ sync gate: Compliance — cleared, current?
  │     ├─ sync gate: Manifest — valid reference?
  │     ├─ domain logic: Declaration.Submit() enforces invariants
  │     │     violation → throw DomainValidationException("BR-DEC-004", …)  ✅ type built
  │     ├─ repository.Update(declaration)          ← does NOT save     ✅ built
  │     ├─ auditWriter.WriteAsync(AuditEvent.Create(…))                ✅ built
  │     └─ dbContext.SaveChangesAsync()   ← UNIT OF WORK, exactly once ✅ built
  ▼ then publish 📤 DeclarationSubmitted                              ✅ mechanism built
```

**The single most important architectural detail here:** `EfRepository` deliberately does *not*
call `SaveChangesAsync`. The Unit of Work stays with the Application layer, which commits **once
per use case**. A submission that touches a Declaration, a Charge, and an audit row commits
atomically or not at all. A repository that saves per call cannot give you that. ADR-0012 names the
anti-pattern it is avoiding in the class's own doc comment.

### API conventions — 📗 FINALIZED (Day 6, `openapi/*.yaml` drafted)

`/api/v1/...` URL-path versioning · `?page=&pageSize=` with a `pagination` block beside `data` ·
comma-separated query filters (OR semantics) · `X-Correlation-Id` on every request and response ·
`409` for state conflicts (e.g. amending a non-draft declaration) · RFC 7807 for every error.

---

# 8. Database Flow

**📗 ER model v1.0 signed off · ✅ Persistence mechanics built · ❌ Zero business tables, zero
migrations.**

### The write path, exactly as it works today

```
Application code
      │  repository.AddAsync(entity)         → DbSet.AddAsync  (tracked, NOT saved)
      │  repository.Update(entity)           → DbSet.Update    (tracked, NOT saved)
      ▼
dbContext.SaveChangesAsync()                 ← the Unit of Work commit, once
      │
      ▼ PlatformDbContext.SaveChangesAsync override        ✅ src/Platform/Persistence/
      │   foreach entry in ChangeTracker.Entries():
      │       if entity is IAuditableEntityMarker:
      │           Added    → CreatedAt = UtcNow
      │           Modified → UpdatedAt = UtcNow
      ▼ base.SaveChangesAsync()  →  Npgsql  →  PostgreSQL 16
```

Two details worth studying:

- **`IAuditableEntityMarker`** is a non-generic marker interface. It exists so the change tracker
  can recognise *any* `AuditableEntity<TId>` regardless of `TId`, without reflecting over open
  generic types. A cheap, clever trick worth stealing.
- **`CreatedAt`/`UpdatedAt` have `internal set`.** Only `PlatformDbContext` (same assembly) can
  write them. No bounded context can spoof its own timestamps.

**Do not confuse the two audit mechanisms.** `AuditableEntity`'s timestamps are lightweight
row-level bookkeeping. `Platform.Audit`'s `AuditEvent` is the *legal* trail — who did what, to
which entity, why, under which business rule, with old→new field values. Different purposes,
deliberately different mechanisms.

### The business schema (📗 FINALIZED design, Day 4 ER v1.0)

```
Manifest ──1:1──► Shipment | CourierShipment ◄──► Party
(IGM/EGM/                     │                  (≥1 of GSTIN/PAN,
 CourierManifest)             │ 1:N               waived for gift+below-threshold)
                              ▼
                          Commodity  (ITC-HS, ControlledGoodsFlag)
                              │ N
                              ▼
                  DeclarationCommodityLine  ← pins CommodityVersion (snapshot!)
                              │ N:1
                              ▼
                        Declaration  (DeclarationType, ManifestId+LineNumber,
                              │       CT / RAT / CUS / MAN)
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
           Charge          Permit      ShipmentIndicator
              └───────────────┴───────────────┘
                              ▼
                   Rule (Category, Priority 1..9)
                              ▼
                   RuleVersion (EffectiveFrom / EffectiveTo)
                       ├──► RuleCondition  (FactName, Operator, Value — AND only)
                       └──► DecisionTrace  (the explainability record)
```

**Four schema decisions and their reasons:**

1. **Snapshot-at-version.** `DeclarationCommodityLine` stores `CommodityId` *plus* the specific
   `CommodityVersion` filed against. A live FK would let a later amendment silently rewrite
   history; charges must stay reproducible for audit years afterwards.
2. **Courier reuses `Declaration`** with a `DeclarationType` discriminator rather than a parallel
   entity — the ShipmentId-or-CourierShipmentId FK pattern already existed, so only the enum needed
   extending.
3. **Party identity is conditional.** At least one of {GSTIN, PAN} for Import/Export/Broker roles —
   *except* gift imports below threshold, where both may genuinely be absent and the Party is
   validated on name/address/ID-document alone. Real regulation, not an edge case to normalize away.
4. **`RuleCondition` is AND-only.** OR is authored as multiple Rules at the same Priority in the
   same Category, since the engine already evaluates all rules at a priority level. A modeling
   technique standing in for a schema feature — cheaper, and revisitable in Phase 3.

### Effective-dating — the mechanism the whole platform rests on

```sql
-- conceptually, how a rule is selected at evaluation time (ADR-0013 §2)
WHERE Rule.Active = true
  AND Rule.Category IN (@categories)
  AND RuleVersion.EffectiveFrom <= @evaluationDate
  AND (RuleVersion.EffectiveTo IS NULL OR RuleVersion.EffectiveTo >= @evaluationDate)
```

Exactly one `RuleVersion` must satisfy this per Rule — enforced as a *data invariant* at authoring
time, not re-checked per evaluation. This is what lets a CBIC notification change policy with no
release, and lets a 2024 declaration still be explained under 2024's rules.

### ❌ Not decided / not built

No EF Core migrations (dev-only `EnsureCreatedAsync`) · no partitioning or archival strategy for
500k shipments/day · no read models/CQRS projections · no DecisionTrace retention policy · and one
**live contradiction**: ER v1.0 §4.5 says **one DecisionTrace row per pipeline run** (embedded JSON
steps, justified by write volume), while ADR-0013 §5 designs **one row per pipeline stage**. ADR-0013
is `Proposed`, so this is open — but resolve it deliberately before Phase 0 Day 31.

---

# 9. Authentication / Authorization Flow

**✅ Mechanism BUILT · ❌ Identity provider undecided.**

```
[1] Login                                    ❌ NOT BUILT — Phase 0 Day 23
      Identity service (build vs. managed provider still open)
      issues a JWT carrying role claims

[2] Request arrives with  Authorization: Bearer <jwt>

[3] UseAuthentication()                      ✅ BUILT
      Validates: issuer, audience, signing key, lifetime (30s clock skew)
      TODAY: symmetric key from config  ("Jwt:Key")
      LATER: Authority-based OIDC discovery once Identity exists

[4] UseAuthorization()                       ✅ BUILT
      Evaluates the endpoint's NAMED POLICY:
        RequireAdmin                     → Admin
        RequireSupervisorOrAdmin         → Supervisor | Admin
        RequireComplianceOfficerOrAdmin  → ComplianceOfficer | Admin
        RequireAnyPlatformRole           → any of the four

[5] Handler runs. Sensitive actions write an AuditEvent (TechnicalAudit).
```

Three design points worth internalising:

**Endpoints reference policy *names*, never role strings.** `RequireAuthorization(PlatformPolicies.
RequireAdmin)` — not `[Authorize(Roles = "Admin")]`. When a fifth role appears, you change the
policy definition once instead of grepping every endpoint.

**Symmetric key today is a scheduling decision, not a shortcut.** `JwtOptions`' doc comment spells
out the migration: switching to `Authority`-based validation changes *how JwtBearer options are
built* — `[Authorize]`, roles, and policies are all unaffected. The library was built now so
everything downstream could proceed; the swap is a config change, not a rewrite.

**It fails fast and loud.** A missing `Jwt:Key` throws at startup with a message pointing at the ADR.
Better a dead app than one silently accepting unvalidated tokens.

❌ Still open: the identity provider itself · secrets management (needs the hosting decision) ·
field-level masking by `DataClassification` at API-response time · DPDPA 2023 consent/erasure
mechanics (ADR-0004 identified the obligation — a gap the PRD never named — but the implementation
is unbuilt).

---

# 10. Background Jobs, Events & Workflows

**✅ In-process pub/sub BUILT · ❌ Real broker undecided · ❌ No job scheduler at all.**

### What works right now

```
Publisher                                    InMemoryEventBus (Singleton)
  DomainEventEnvelope<T>.Create(...)   ──►    creates a DI SCOPE per publish
  eventPublisher.PublishAsync(envelope)       resolves ALL IEventHandler<T>
                                              invokes each, sequentially, in-process
```

The scope-per-publish is not incidental. The bus is a Singleton (a bus must outlive requests), but
handlers are typically Scoped (they hold a `DbContext`). Resolving Scoped from the root provider
throws. Creating a scope per publish is what a real broker does per delivered message — so this is
the *correct* design, not a workaround.

**What it deliberately does not do:** survive a restart · cross a process boundary · retry ·
dead-letter · guarantee ordering · deduplicate. It proves the *contract*; it is not the
implementation. Swapping in Kafka/RabbitMQ/ASB means one new `IEventPublisher` registration inside
`AddCustomsClearanceMessaging()` — the envelope shape and `IEventHandler<T>` contract don't change.

### The 30 catalogued domain events (📗 FINALIZED, Day 5 §3)

| Origin | Events |
|---|---|
| Shipment Mgmt | `ShipmentCreated`, `ShipmentValidated`, `ShipmentValidationFailed`, `JourneyCreated`, `LegDeparted`/`LegArrived`, `CurrentLegChanged`, `ShipmentIndicatorAdded` |
| Segregation | `ClearanceTypeAssigned`, `ReSegregationTriggered`, `ManualOverrideApplied` |
| Commodity / HS | `CommodityCreated`, `CommodityClassified`, `CommoditySplit`/`Merged`, `CommodityVersionCreated` |
| Duty & Tax | `ChargesCalculated`, `ChargesRecalculated` |
| Permit / Compliance | `PermitRequired`, `PermitStatusChanged`, `ComplianceFlagRaised`, `ComplianceClearedForSubmission` |
| Manifest | `ManifestFiled`, `ManifestAmended` |
| Declaration | `DeclarationCreated`, `DeclarationVersionCreated`, `DeclarationReadyForSubmission`, `DeclarationSubmitted`, `DeclarationAmended`/`Cancelled` |
| Customs Gateway | `CustomsResponseReceived`, `ClearanceGranted` |
| Workflow | `BrokerTaskCreated`, `StatusTransitioned` |

### Long-running workflows

**📗 FINALIZED as a responsibility (context #10), ❌ zero design.** The Workflow Engine subscribes
broadly and owns the four status machines — CT (clearance type), RAT (rating), CUS (customs), MAN
(manifest) — plus broker task assignment and escalation. Whether it's a state machine, a saga
library, or hand-rolled: undecided.

**❌ Completely absent:** any background job scheduler. Yet the flow needs several — ICEGATE
response polling, retry-per-configured-policy for system errors (Ch.3 Stage 11), exchange-rate
refresh, tariff-update-driven re-classification, permit-expiry monitoring, SLA escalation timers.
Nothing named, nothing chosen. Worth flagging early.

---

# 11. External Integrations

**📗 Identified · ❌ None built, and the most important one isn't even confirmed.**

```
        UPSTREAM (in)                              DOWNSTREAM / PEER (out)
  ┌──────────────────────┐                   ┌──────────────────────────────┐
  │ Logistics / freight  │──shipments───►    │ ICEGATE (CBIC)               │
  │ management systems   │                   │  declarations, acks, responses│
  └──────────────────────┘                   │  ⚠ TARGET AUTHORITY UNCONFIRMED│
  ┌──────────────────────┐                   └──────────────────────────────┘
  │ ITC-HS tariff master │──rates───►        ┌──────────────────────────────┐
  │ ⚠ MUST BE LICENSED   │                   │ DGFT — licence validation     │
  └──────────────────────┘                   ├──────────────────────────────┤
  ┌──────────────────────┐                   │ OGAs — controlled-goods appr. │
  │ Exchange rates       │──FX───►           ├──────────────────────────────┤
  └──────────────────────┘                   │ Downstream logistics — release│
                                             │ Financial/ERP — postings      │
                                             └──────────────────────────────┘
```

**Two integration risks deserve to be on your radar permanently:**

1. **The ITC-HS tariff master is an external hard dependency.** Day 1 §4.1 states the HS
   Classification Engine cannot be considered functionally complete until India's tariff master is
   *sourced and licensed* — a commercial/legal task, not an engineering one, flagged as a Phase 1
   workstream. It can be built structurally against placeholder data (Phase 0 Day 39 seeds
   placeholders only), but it is not a thing you can code your way past.

2. **The target customs authority is still unconfirmed.** ICEGATE is assumed throughout, but Day 1
   §4.2 and the plan's reminder flags both note the confirmation is *pending from the user*.
   ICEGATE certification and sandbox access are explicitly Phase 8. This is why ADR-0006 wraps
   government messages in an internal `AdapterVersion` decoupled from the government's own
   versioning — the Customs Gateway context is designed as an **anti-corruption layer** precisely
   because that dependency is both uncertain and outside our control.

The standing policy for everything else — WMS, TMS, ERP, HR, CRM, accounting — is **integrate by
API, never build in-platform** (PRD Ch.1).

---

# 12. Deployment & Infrastructure Architecture

**✅ Local/CI environment BUILT · ❌ No hosting target chosen — deliberately (ADR-0008).**

### What exists and runs today

```
   docker compose up
        │
        ├── api  (customsclearance-api:dev)
        │     multi-stage Dockerfile: SDK builds, aspnet runtime runs
        │     :8080,  ASPNETCORE_ENVIRONMENT=Development
        │     depends_on: postgres  condition: service_healthy   ◄── not optional, see below
        │
        └── postgres:16-alpine
              healthcheck: pg_isready, every 2s
              volume: postgres-dev-data
```

**Why `condition: service_healthy` is there:** list-form `depends_on` waits only for a container to
*start*. Postgres's first-time init includes an internal restart, so the Api connected too early
and the unhandled failure killed the whole process. Fixed at the orchestration level *and* with a
5-attempt retry loop in `Program.cs` — two layers, because the compose fix doesn't help someone
running `dotnet run` against an already-up-but-busy database.

### The pipeline

```
  push / PR to main
        ▼
   ┌─────────┐
   │  build  │  build.sh
   └────┬────┘
        ├──► lint              dotnet format --verify-no-changes
        ├──► test              tests/Unit/** + coverlet thresholds → HARD GATE
        ├──► integration-test  WebApplicationFactory, in-process, NO database
        ├──► containerize      compose up → /health → real Postgres round-trip
        └──► contract-test     Newman/Postman over real HTTP
```

Every job is one line: `bash build/scripts/<x>.sh`. All logic lives in portable shell so the
workflow can move to Azure DevOps (`build/azure-pipelines.yml` is parked in lockstep) by changing
only orchestration syntax. Static analysis runs via `SonarAnalyzer.CSharp` as a Roslyn analyzer in
`Directory.Build.props` — Sonar's rule set at build time, **with no SonarCloud server required**.

### ❌ What is deliberately not decided

**No cloud provider. No hosting target. No Kubernetes-vs-PaaS call. No staging or production
environment.** ADR-0008 defers all of it to Phase 0 Day 21, and Day 21's own wording keeps the door
open: "local/dev Kubernetes **(or Docker Compose for solo-scale dev)**."

This deferral is load-bearing, and it cascades: the hosting choice determines the secrets manager,
which determines JWT key management; it influences the event-bus choice (Azure Service Bus only
makes sense on Azure); it determines the observability stack and the deployment topology. **One
undecided thing is currently blocking five others** — which is exactly why it should be decided
early in Phase 0 rather than drifted into.

📗 The promotion path *is* settled in principle: **dev → staging → prod**. Only dev exists.

---

# 13. The Mental Model in One Page

The sentence you asked for — "User does X → UI calls Y → backend does Z → domain does A → DB
changes B → event C happens" — for the single most important flow in the platform:

```
━━ SEGREGATION: how a shipment gets its Clearance Type ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  TRIGGER   An upstream logistics system posts a shipment
            (or a broker edits an HS code, re-triggering this whole flow)
              │
  API        POST /api/v1/shipments        📙 endpoint designed, not built
              │  → CorrelationIdMiddleware stamps X-Correlation-Id   ✅ BUILT
              │  → JWT validated, RequireAnyPlatformRole checked     ✅ BUILT
              ▼
  DOMAIN     Shipment Management validates mandatory fields
              │  a failure → throw DomainValidationException("BR-SHP-003")  ✅ type BUILT
              │              → 422 + RFC 7807 + correlationId               ✅ BUILT
              ▼
  DATABASE   repository.AddAsync(shipment)      ← tracked, not saved   ✅ BUILT
             dbContext.SaveChangesAsync()       ← UoW, once            ✅ BUILT
              │  PlatformDbContext stamps CreatedAt automatically      ✅ BUILT
              ▼
  EVENT      📤 ShipmentValidated                                       ✅ envelope BUILT
              │  DomainEventEnvelope carries the SAME correlationId
              ▼
  REACTION   Segregation Engine handles it                             📙 DESIGNED
              │
              ├─ IFactsProvider    assembles ~13 facts (channel, purpose,
              │                    importer type, HS code, value, FTZ, flags…)
              ├─ IRuleSetLoader    loads rules effective ON the evaluation date
              │                    ⇒ this is what makes CBIC notifications
              │                      deployable without a release
              ├─ IConditionEvaluator  a rule fires only if ALL conditions match
              ├─ IPriorityResolver    lowest Priority number wins (1 = Legal)
              │                       ties broken deterministically by RuleId
              └─ IDecisionTraceRecorder  writes a trace row for EVERY stage,
                                         including stages where nothing matched
              │
              │  …repeated across 11 sequential stages (Country → Customer →
              │  Shipment → Commodity → HS Code → Duty → Permit → Indicators →
              │  Warning → Final CT → Status), with Facts MUTATED between them
              │  so a Permit rule can see what an HS Code rule just decided
              ▼
  RESULT     CT assigned  ·  📤 ClearanceTypeAssigned (+ DecisionTraceId)
              │
              ▼
  CASCADE    Declaration auto-created → commodities classified → duty calculated
             → permits verified → compliance validated → 3 sync gates
             → submitted to ICEGATE → response processed → released
```

**Read that flow twice.** Every architectural decision in this repository is visible in it: the
correlation ID threading everything; the repository/UoW split; the event-driven default with sync
gates only at the legal boundary; effective-dated rules instead of hardcoded thresholds; and a
decision trace written even when nothing happens — because "no rule matched" is itself an answer
you must be able to defend to a customs auditor.

---

# 14. Status Scorecard

| # | Area | Status | The one open item that matters most |
|---|---|---|---|
| 1 | Purpose & goals | 📗 Finalized | — |
| 2 | 17 bounded contexts | 📗 Boundaries finalized · ❌ 0 built | No context has an internal design yet |
| 3 | Technical components | ✅ 7 Platform libs · ❌ all services | — |
| 4 | Communication | 📗 Finalized (unusually thorough) | — |
| 5 | Data flow | 📗 12 stages finalized · ❌ 0 built | — |
| 6 | User interaction | ❌ Largely undecided | **No frontend decision at all** |
| 7 | Backend/API | 📗 Standards · ✅ middleware · ❌ endpoints | — |
| 8 | Database | 📗 ER v1.0 signed off · ❌ no tables/migrations | DecisionTrace granularity contradiction |
| 9 | Auth | ✅ Mechanism · ❌ provider | Identity: build vs. buy (Day 23) |
| 10 | Events & jobs | ✅ In-process · ❌ broker, ❌ scheduler | **No job scheduler even identified** |
| 11 | Integrations | 📗 Identified · ❌ none built | Tariff licensing + unconfirmed authority |
| 12 | Infrastructure | ✅ Local/CI · ❌ no hosting target | **Blocks secrets, broker, observability** |

### If I were prioritising the next decisions

1. **Hosting target** (Phase 0 Day 21) — it unblocks secrets management, the event-bus choice, and
   the observability stack. One decision, four dependencies cleared.
2. **DecisionTrace granularity** — resolve the ER v1.0 §4.5 ↔ ADR-0013 §5 contradiction *before*
   Day 31 builds the schema, not during.
3. **Background job scheduler** — currently unnamed, yet Ch.3 Stage 11's retry policy, ICEGATE
   polling, and SLA escalation all require one.
4. **Frontend direction** — the largest untouched surface in the whole plan.
5. **ITC-HS tariff licensing** — a commercial lead time you cannot engineer around; start it early.

---

## Source Traceability

| This document says | Comes from |
|---|---|
| Purpose, 12-stage process, SLAs, clearance types | `CustomsClearance_PRD.docx` Ch.1, 3, 4, 5 |
| 17 bounded contexts, de minimis constraint, personas | `Day1_Bounded_Context_Map_v2.docx` |
| Entity model, snapshot-at-version, Manifest gap, AND-only | `Day4_ER_Diagram_v1.0.docx` |
| Event catalog, event-vs-direct-call, Direct Call Register | `Day5_Context_Map_and_Domain_Event_Catalog_v1.1.docx` |
| Gateway routing, unified versioning, event envelope | `Day6_API_Standards_v1.1.docx` |
| Rules engine pipeline & interfaces | `adr/0013` (**Status: Proposed**) |
| Phase 0 day-by-day plan | `CustomsClearance_India_DayWise_Plan_Phase-1_and_Phase0.docx` |
| Everything marked ✅ BUILT | `src/`, `tests/`, `build/`, `docker-compose.yml`, `.github/workflows/ci.yml` |

**Companion document:** `LEARNING_NOTES.md` — the same system from the *code* upward (patterns,
module internals, the five Day-18 bugs, what to study). This document is the top-down view; that
one is bottom-up. Read them together.

*Verified against commit `bcc353c`. Day 19 of 50 complete.*
