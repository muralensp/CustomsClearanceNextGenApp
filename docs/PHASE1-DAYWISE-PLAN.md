# Phase 1 Day-Wise Plan (DRAFT for Product Owner review)

Status: **draft, revised 2026-10-22 after review and after reading the original plan documents**. Days continue from Day 51 and now end at Day 107. A "day" is one unit of work that ends
with its verification evidence and an `Execution_Log.md`; it is not a calendar day, and no calendar dates are
promised. The original plan estimates Phase 1 at 4-6 months (`CustomsClearance_Phasewise_DevlopmentPlan_v2.docx`,
section 4), but that figure assumed dedicated teams per track. For a solo Product Owner plus Claude it is an
assumption, not a commitment: elapsed time per day is recorded through M1 and a re-based estimate is published at the
Day 61 re-plan.

Sources: `docs/PHASE1-DECISIONS.md`, ADR-0043 to ADR-0049, `docs/PHASE1-KICKOFF-BRIEF.md`,
`docs/security/PHASE0_SECURITY_CHECKLIST.md` (Phase 1 revision), `docs/WORK-ITEMS.md`.

Sources read for the reconciliation: `CustomsClearance_Phasewise_DevlopmentPlan_v2.docx` and
`CustomsClearance_India_DayWise_Plan_Phase-1_and_Phase0.docx` (see the Reconciliation section at the end).

Revisions from review: a schema-evolution day was added (Day 53) because the repo has no EF migrations and creates
schemas only in Development (`DatabaseBootstrap.EnsureSchemaAsync`); the cloud decision moved to just after M2;
`IsVerified` moved to Block D; Gate 3, contract-test and AsyncAPI steps were added; ADR numbers are reserved.

## Reserved ADR numbers

| ADR | Purpose | Used on |
|---|---|---|
| 0050 | Schema evolution and migrations (per context, grants, RLS, production DBA/IaC path) | Day 53 |
| 0051 | UI lives in `web/` in this monorepo (folder rules, two-window workflow) | Before Day 96 |
| later | Framework confirmation; any new decision found while building | assigned up front by the orchestrator |

## Rules for every day (from CLAUDE.md, not repeated per row)

- One day at a time; the next day starts only on explicit instruction. Commit and push only when asked.
- Every change goes branch, PR, `CI passed` + `Analyze (csharp)`, squash or rebase.
- Nothing is done until proven on the real stack (compose, real HTTP, real unpiped exit codes), with fault injection
  for each new control (prove it can fail).
- Each day gets an `Execution_Log.md`; an ADR where a new decision is made; a one-line status in CLAUDE.md only at
  milestones. Phase 1 does not produce the Phase -1 narrative `.docx`.
- Existing E2E (21 checks), integration and contract suites stay green after every day that touches auth or data.
- **Every day that adds or changes an endpoint** also updates `openapi/` and extends the Postman/Newman collection
  (currently one request). Every day that adds an event updates `asyncapi/`.
- Timings in logs are verified from outputs, not narrative (standing lesson).

## Block A - Tenancy foundation (ADR-0043, ADR-0046). Work item PLAT-003

| Day | Deliverable | Proof | Depends on |
|---|---|---|---|
| 51 | Inventory every table and DbContext in `src/`; classify each as tenant-owned, global or layered; design note on RLS with the `audit_writer` role | Inventory checked against the real schema in Postgres, not only the code | - |
| 52 | `ITenantContext` port; `tenant_id` claim added to the dev login; Gateway and Api resolve it; fail closed with no tenant | Unit tests; live request with and without the claim | 51 |
| 53 | **Schema evolution (ADR-0050):** adopt EF Core migrations (or versioned SQL) per context; baseline the current schema; `audit_writer` grants and RLS policies as part of the scripts; Development bootstrap kept only as a convenience; production provisioning path documented | A fresh database and an already-populated Phase 0 database both reach the same schema; second run is a no-op | 51 |
| 54 | `Tenant` table, default tenant, platform-admin role (outside any tenant) in dev seed | Migration applies to fresh and existing databases | 52, 53 |
| 55 | `TenantId` and EF global filters on tenant-owned tables (users, notifications, delivery, decision traces, tenant config) | Filter unit tests; E2E still green | 54 |
| 56 | RLS policies; tenant set with `SET LOCAL` in the unit-of-work layer; zero rows when unset | Pooled-connection test (A then B on one connection); direct psql check as a restricted role | 55 |
| 57 | Audit table tenant column and RLS compatible with `audit_writer`; **isolation test that fails when a tenant-owned table lacks the column or a policy** | Fault injection: add a table without isolation and watch the test fail | 56 |
| 58 | Event envelope mandatory `TenantId` (versioned); publisher fills it; consumers set context; dead-letter on missing tenant; first `asyncapi/` entry | Consumer test with a tenant-less envelope; live RabbitMQ run | 56 |
| 59 | Layered config and rules: `scope` marker, mandatory flag, precedence, decision trace records the layer | Tenant cannot shadow a mandatory rule (test that fails if allowed) | 55 |
| 60 | Transactional tenant creation (tenant, root node, legal-entity record, first admin record without a Keycloak account yet), tenant suspension | Fault injection mid-creation leaves nothing behind; suspended tenant blocked. **The Keycloak invite is completed on Day 76** | 54, 57 |
| 61 | **Milestone M1 verification:** E2E extended for tenancy, contract suite, k6 re-run against Day 42 (about 20% p95 trigger, proposed; confirm the Day 42 scenarios still run with a `tenant_id` claim), Gate 1 and Gate 2 re-check, checklist 8.1-8.3, 8.5, 8.6; **Database-layer exit line** (schema review, migrations validated on fresh and populated databases, integrity checks, performance baseline) | All suites green; k6 numbers compared in the log | 51-60 |

## Block B - Identity: Keycloak (ADR-0044). Work item PLAT-004

| Day | Deliverable | Proof | Depends on |
|---|---|---|---|
| 62 | Keycloak (pinned version) in compose with healthcheck and `service_healthy`; checked-in realm export; **verify Organizations support in the pinned version** and record the attribute-vs-Organizations choice | Cold start repeated several times (RabbitMQ-style first-boot flakiness is a known risk) | M1 |
| 63 | `IIdentityProvider` abstraction; in-house adapter; `PlatformUser` stores Keycloak `sub`; per-request resolution of `sub` to user, tenant and status; no just-in-time creation | Unknown `sub` rejected; suspended user blocked at once | 62 |
| 64 | Keycloak adapter (create, invite, disable, enable, reset); protocol mapper for `tenant_id`; Gateway and Api trust the Keycloak issuer; JWKS caching. **Heavy day** | Real login through Gateway and Api | 63 |
| 65 | Production guard for the in-house login: startup guard, guarded route mapping, no Gateway route, dev seed only in Development and CI | Production plus flag on refuses to start (exit code captured unpiped); in-house-signed token rejected in the Keycloak profile | 64 |
| 66 | **Identity E2E CI job** (own compose profile) with the full check list: `tenant_id` present, suspended user rejected, tenant A cannot read tenant B. **Heavy day** | Each check shown to fail: tampered signature, wrong issuer, expired token, missing `tenant_id` | 64, 65 |
| 67 | Shape-pinned fast test plus contract test against a captured real-realm token; MFA mandatory in the realm (TOTP, WebAuthn, no SMS) | MFA-less production user cannot obtain a token | 66 |
| 68 | Suspension propagates to Keycloak with retry and alert; Keycloak outage behaviour documented; runbook | Kill Keycloak mid-run; existing tokens work, new logins fail, as documented | 67 |
| 69 | **Milestone M2 verification:** E2E on in-house login still 21+ checks; identity E2E green; checklist 1.2, 1.6-1.8, 1.9, 2.1 updated; Gate 1 and Gate 2 re-check; Database-layer exit line | All suites green | 62-68 |

**Product Owner decision due by Day 70: the cloud choice (ADR-0047).** It is a decision, not a work day. It is taken
here because secrets, database TLS and Keycloak operations all depend on it; the hardening work stays in Block E.

## Block C - Authorization, users, organization (ADR-0045). Work item PLAT-005

| Day | Deliverable | Proof | Depends on |
|---|---|---|---|
| 70 | Permission catalogue; default roles seeded to match the four existing roles; endpoint policies move from role names to permissions | Existing 401/403 matrices and E2E unchanged | M2 |
| 71 | Permissions resolved per request (not in the JWT); role and permission changes audited | Revoked permission takes effect on the next request | 70 |
| 72 | Org tree model (Enterprise to Team) with legal-entity nodes, using the Day 53 migration mechanism | Real Postgres | M2 |
| 73 | Org admin API with audit; OpenAPI and Newman updated | 401/403 matrix; audit rows live | 72 |
| 74 | Role assignments scoped to org nodes; scope enforcement | Branch supervisor cannot see another branch (fails without enforcement) | 71, 73 |
| 75 | Separation-of-duties exclusion structure (data model only, no rules enforced) | Migration and unit tests | 71 |
| 76 | User CRUD with app-driven provisioning through `IIdentityProvider`; lifecycle Provisioned > Active > Suspended > Deactivated, audited; **completes the Day 60 tenant-creation invite** | Live create-to-login on Keycloak; tenant creation now also creates the first admin's Keycloak account | 64, 74 |
| 77 | Suspend, deactivate, admin-mediated MFA recovery (tenant admin; platform admin for tenant admins) | Audited; recovery path tested | 76 |
| 78 | Service accounts (client credentials, narrow scopes); design note only for customers and partners (PRD 20.4) | Service token works and is scoped | 76 |
| 79 | Tenant admin APIs (tenant settings, retention setting); platform admin APIs (tenants, reference data) | Platform actions audited under the `platform` tenant | 60, 76 |
| 80 | **Milestone M3 verification:** E2E extension for users, org, permissions; contract suite; Gate 2 and **Gate 3 (API review)**; checklist 8.4, 8.7; Database-layer exit line | All suites green | 70-79 |

## Block D - Audit, privacy, notification, configuration, tariff (ADR-0046, 0048, 0049). Work items PLAT-006, PLAT-007

| Day | Deliverable | Proof | Depends on |
|---|---|---|---|
| 81 | Pseudonymous subject id in audit rows; identifying details in a separate erasable table | Erasure removes details, audit trail intact | M3 |
| 82 | Audit partitioned by date; per-tenant retention setting (8-year provisional default); expiry by partition drop designed and rehearsed in dev | Partition drop on dev data; nothing expires under the floor | 81 |
| 83 | Notification events carry a user id; dispatcher resolves the address at send time; stored address removed | Address no longer in the RabbitMQ message or table (checklist 4.4, 6.2) | M3 |
| 84 | Notifications tenant-aware; per-user preferences; templates | Tenant A cannot read tenant B notifications | 83 |
| 85 | Delivery retry and dead-letter hardening | Fault injection on SMTP | 84 |
| 86 | Configuration layering admin API; configuration versioning (PRD 21.11) | Version history audited | 59 |
| 87 | `IsVerified` records who and in what capacity; PO-verified rows block live filing (flag exists, filing does not yet) | Test that a PO-only row is rejected for filing use | 79 |
| 88 | **Sourcing decision document (India)** for three data sets: ITC-HS tariff, exchange-rate source (customs valuation; the notified source is not yet verified) and lookup tables (UQC units, ports, countries, currencies). The effective-dated schema stays in Phase 1; **the loader and real ingestion are Phase 2** (original plan section 2.4) | Document reviewed by the Product Owner; each source marked verified or unverified | 87 |
| 89 | Offboarding as states; read-only export mode for a suspended tenant; offboarding procedure document | Suspended tenant admin can export, nothing else | 60 |
| 90 | Data-subject request runbook (tenant admin as fiduciary side) | Walkthrough on dev data | 81 |
| 91 | **Milestone M4 verification:** Gate 2 and Gate 3; checklist 4.4, 5.6, 6.2, 6.4, 8.8 updated; Database-layer exit line; E2E and contract green | All suites green | 81-90 |

## Block E - Backend system tests, edge hardening, UI

The original plan's sequence for Phase 1 is "Backend first, then Testing, then UI". Days 92-93 are that testing stage.

| Day | Deliverable | Proof | Depends on |
|---|---|---|---|
| 92 | **System tests:** full regression of all suites on a cold stack; k6 against the Day 42 baseline; **tenant-isolation sweep**: every endpoint called with tenant A's token must never return tenant B's data, and the sweep fails when an endpoint is not covered | Real unpiped exit codes; the sweep shown to fail when an endpoint is left out | M4 |
| 93 | **Security and database exit:** dependency and secret scans; full Database-layer exit review (schema review, validated migrations, integrity, performance baseline); manual authorization negative testing across roles, permissions and org scope. DAST and the penetration test stay scheduled before Phase 8 (checklist 7.11) and are not part of Phase 1 | Evidence in the log; findings fixed or accepted with reasons | 92 |
| 94 | Security hardening needing hosting: TLS at the edge, HSTS and security headers (7.3), secrets store, key rotation, DB TLS; revised ADR-0008 rows; closes PLAT-002 | Live where possible; otherwise stated as unproven | cloud choice (due Day 70) |
| 95 | Gateway BFF: OIDC code flow, cookie session, CSRF protection; bearer path unchanged | Browser-style session test; tokens never reach the client | M2, 94 |
| 96 | `web/` ADR-0051 and framework confirmation (React + TypeScript provisional); scaffold; CI job | Build and lint in CI | 95 |
| 97-102 | UI, one area per day: session and login, user admin, org admin, role admin, tenant admin, audit viewer | Gate 4 checklist per screen; real backend | 96 |
| 103 | UI: configuration admin and notification inbox | As above | 102 |
| 104 | Frontend tests and accessibility pass | Gate 4 | 103 |
| 105 | Phase 1 regression pass (all suites, k6, identity E2E, containers cold) | Real unpiped exit codes | all |
| 106 | Gate 1, Gate 2, Gate 3 re-check and Gate 4 for the UI; security checklist final | Evidence in the log | 105 |
| 107 | Phase 1 sign-off note for the Product Owner | PO decision | 106 |

## Work items to days

| Work item | Days |
|---|---|
| PLAT-003 tenancy foundation | 51-61 |
| PLAT-004 identity | 62-69 |
| PLAT-005 authorization, users, org | 70-80 |
| PLAT-006 audit and privacy | 81-82, 89-90 |
| PLAT-007 tariff, `IsVerified` | 87-88 (plus the Product Owner actions below) |
| PLAT-002 hosting | decision by Day 70, work on Day 94 |

## Parallel non-code track (Product Owner actions; Claude can draft)

| Action | Needed by | Source |
|---|---|---|
| Obtain the official ICES specification and onboarding rules | Before Phase 3 sandbox requests; start now | ADR-0049, PLAT-007 |
| Confirm Keycloak Organizations support (done by Claude on Day 62) and ICEGATE signing rules | Day 62 (Keycloak); before Phase 3 (signing) | ADR-0044, ADR-0049 |
| **Choose the cloud** | **Day 70** | ADR-0047 |
| Collect tariff vendor quotes and redistribution terms against the INR 1,00,000 ceiling | Before the Day 88 sourcing decision document | ADR-0049 |
| Legal check: retention minimums, processor/fiduciary split | Before production | ADR-0048 |

## Risks and honest caveats

- **Sizing is uncertain.** Heavy days: 53, 56, 64, 66, 76, 92, 95 and the UI days. Past reported durations ran 2-4x too optimistic, so re-plan at each milestone rather than trust the whole list.
- **The cloud decision (due Day 70) gates Day 94.** If it slips, TLS, headers and key rotation stay unproven and the UI proceeds only partly.
- **Day 53 changes how every later schema change is made.** If the migration approach proves wrong, Days 54-60 and 72 are affected.
- **Keycloak first-boot time** is a new flake risk (the RabbitMQ first-boot race recurred twice on Day 48).
- **Single approver and single platform admin** remain known gaps (checklist 8.7).
- **Scope removed on purpose:** tasks, SLAs, escalation and work queues (PRD ch. 16) wait for the first phase with a real workflow.
- **Not planned here:** billing, customer and partner identity types beyond service accounts, SoD rule enforcement, tenant-editable custom roles, self-service sign-up, FTA rates. The UI runs after the backend blocks as drafted; running it in parallel was discussed and deliberately left out.
- **Process debt carried:** cross-context `ProjectReference` check is manual (automate before the first real context); test-name convention only partly followed; Api endpoint logic has no measured coverage; the Azure DevOps YAML has never run.
- Re-planning points: after Day 61, 69, 80, 91 (milestones M1-M4). The Day 61 re-plan also publishes the re-based estimate (see the intro).

## Reconciliation with the original plan (read 2026-10-22)

| # | Original plan | Resolution in this plan |
|---|---|---|
| 1 | Phase 1 sourcing covers tariff, exchange-rate feeds and lookup tables as licensing and ingestion *planning*; ingestion is Phase 2 | Day 88 is a sourcing decision document for all three; loader and ingestion move to Phase 2 |
| 2 | Open item: confirm jurisdictions for MVP tariff sourcing | **India confirmed** by the Product Owner |
| 3 | Tasks, SLA, Escalation, Collaboration are Phase 5 modules | Deferred to Phase 5 (wording in `PHASE1-DECISIONS.md` corrected) |
| 4 | Backend, then Testing, then UI | Days 92-93 are the backend testing stage before the UI |
| 5 | Database layer exit: migration scripts, validated migrations, integrity, performance baseline | Day 53 introduces migrations; a Database-layer exit line is in every milestone and a full review is on Day 93 |
| 6 | Phase 0 listed Kubernetes; Phase 0 closed GO on Docker Compose | Recorded as a Phase 0 gap; orchestration is decided with the cloud choice (due Day 70); no Kubernetes work in Phase 1 |
| 7 | 4-6 months assumed dedicated teams; team topology open | Timeline measured, not promised; re-based at the Day 61 re-plan |
| 8 | Agency sandbox requests start in Phase 3 | The ICES specification is requested now (earlier, no conflict) |

Also from the original plan, unchanged: UI after backend gates (section 1.1), the 95%/80% coverage gates (ADR-0007),
Gates 1-4 for Phase 1 (Gate 5 is a release gate and does not apply), and the "no frontend before backend passes its
gates" rule. If the Product Owner later wants the UI to run in parallel with the backend, section 1.1 of the original
plan must be amended first.
