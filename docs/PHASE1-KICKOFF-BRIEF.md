# Phase 1 Kickoff Brief — Enterprise Core (decisions taken 2026-10-22; see `docs/PHASE1-DECISIONS.md`)

Prepared on Day 49 (2026-10-19). Status: **decisions 1-5 and 8 taken on 2026-10-22** in a Product Owner interview (ADR-0043 to ADR-0049); sections below are kept as written on Day 49. Decisions 6-7 are unchanged (India only; ICEGATE unconfirmed until the ICES specification is obtained). It states what Phase 1 is, what already exists, and the
decisions needed before work starts. Items marked **Proposal** are my recommendations, not decisions.
Sources: `CustomsClearance_Phasewise_DevlopmentPlan_v2.docx` (section 2.3), PRD chapters 16, 20 and 21,
ADR-0017/0031/0032/0030/0036/0041/0042, `docs/security/PHASE0_SECURITY_CHECKLIST.md`, and the Day 46-48 logs.

## 1. Scope (from the plan)

Phase 1 = **User Management, Organization, Security, Notification, Audit, Configuration**, sequenced
"Backend first, then Testing, then UI", with the **Tariff & Reference Data Sourcing** workstream starting in
parallel. Estimated 4-6 months in the plan. The day-wise plan stops at Day 50; a new day-wise plan for Phase 1
is to be written after sign-off, based on what Phase -1 and Phase 0 taught us.

## 2. What Phase 0 already gives Phase 1 (verified Days 46-48)

| Phase 1 area | Exists today | Still missing (PRD reference) |
|---|---|---|
| User Management | `PlatformUser` table, seeded dev personas, `POST /auth/login`, role JWTs (ADR-0017); roles Broker, Supervisor, ComplianceOfficer, Admin | User CRUD, identity lifecycle Provisioned→Suspended→Deactivated with audit (20.5), other identity types (customers, partners, service accounts, 20.4), password reset |
| Organization | nothing | Hierarchy Enterprise→Country→Region→Branch→Department→Team, multiple legal entities (21.5) |
| Tenancy | **no tenant concept anywhere in `src/`** | Tenant isolation of data, audit logs, keys (20.10); tenant administration (21.6) |
| Security | JWT at Gateway and Api, role policies, audit immutability, JWT key guard, `/metrics` internal-only | MFA (20.7), lockout, security headers, TLS, key rotation, retention, DPDPA (checklist 1.7, 1.8, 2.6, 3.x, 5.6, 6.4, 7.3) |
| Notification | event → in-app + SMTP, delivery records, `/api/notifications` (ADR-0031) | Task management, escalation, SLA, collaboration, work queues (ch. 16); recipient address no longer in event payload (checklist 6.2) |
| Audit | durable append-only trail, DB-enforced, config changes audited (ADR-0032, 0036) | Rules-admin mutations unaudited (Day 47 gap), per-context audit, retention/partitioning, Confidential-field access audit |
| Configuration | key/value + feature flags, runtime change, audited (ADR-0030) | UI-facing admin, configuration versioning (21.11), per-tenant config |
| UI | none | No frontend technology is chosen in any ADR |

## 3. Decisions needed from the Product Owner (in dependency order)

1. **Tenancy model (blocks everything below).** Single-tenant per deployment, or multi-tenant SaaS with a tenant
   key on every row? PRD 20.10 allows both. Retrofitting a tenant key after User, Organization and Audit tables
   exist is the most expensive mistake available in this phase. **Proposal:** decide first and write it as an ADR;
   if undecided, default to carrying a `TenantId` column from the first Phase 1 table.
2. **Identity source.** Keep the in-house login (ADR-0017) or adopt an external IdP/SSO. This decides MFA,
   lockout, password reset and refresh tokens (checklist 1.7, 1.8, 1.6). **Proposal:** choose before building user
   CRUD, since an IdP removes most of it.
3. **Hosting target** (PLAT-002, ADR-0008). Gates TLS, encryption at rest, secret storage and key rotation.
4. **Frontend technology.** Needed before the UI step; not needed for backend-first work.
5. **Tariff-sourcing answers** (ADR-0042 section 5): does the platform file with ICEGATE or only advise; is a paid
   subscription acceptable and at what budget; is FTA/preferential rate data needed in Phase 1; who owns the
   `IsVerified` sign-off.
6. **Jurisdictions in scope for MVP tariff sourcing** (plan section 5, drives licensing lead time). Working
   assumption since the beginning: India only.
7. **Target customs authority** (ICEGATE assumed, still unconfirmed): only needed to start sandbox requests in
   Phase 3, no action now.
8. Compliance decisions already open: audit retention period (5.6), DPDPA data-subject handling (6.4).

## 4. Tariff sourcing — plan inputs

State: ADR-0042 is Proposed and records no decision. The research note
(`docs/research/INDIA-TARIFF-SOURCING-OPTIONS.md`) recommends evaluating a hybrid: official CBIC/DGFT documents
for the code list and ITC-HS status, with a commercial feed or the ICEGATE calculator only to cross-check rates.
It rests on web search alone; no official site or licence was read, and every *(unverified)* item must be
confirmed. Inputs ready for Phase 1: effective dating on every tariff row, a repeatable load process, the
`IsVerified` flag with a named owner, and awareness that HS 2028 will force a re-mapping. Next actions (need the
answers in 3.5): collect vendor quotes and redistribution terms; check data.gov.in; confirm the Customs Tariff
edition from an official source. The Day 39 reference data stays placeholder until then.

## 5. Risks and carry-forward backlog

- **Tenancy and identity decisions late** → rework across User, Organization, Audit (highest risk).
- **Tariff licensing lead time**, an external dependency (plan section 2.3).
- **Process debt**: cross-context `ProjectReference` check is manual (automate before the first real context);
  `asyncapi/` empty (populate with the first domain event); `docs/WORK-ITEMS.md` unused since Day 13 (restart it);
  tests named `Method_Scenario_Expected` only 73 of 166 times; Api endpoint logic has no measured coverage;
  private-setter rule untested until the first domain entity.
- **Infra flake**: RabbitMQ first-boot race recurred twice on Day 48; `contract-test.sh` leaves the stack running
  if `up` fails (trap registered too late); the Newman collection is thin (1 request).
- **Never run anywhere**: the Azure DevOps YAML.
- **Single approver** for every gate; there is no second reviewer, so the written logs are the control.

## 6. Proposed shape of the first weeks (Proposal, not agreed)

1. Tenancy ADR and identity-source ADR (decisions 1-2), plus a Phase 1 ADR for the Organization model.
2. Close the small Phase 0 follow-ups before new modules: audit the rules-admin mutations, move the teardown
   `trap`, restart `WORK-ITEMS.md`.
3. User Management backend (after decision 2), then Organization, then Notification task/escalation, each through
   Gate 1 and Gate 2 and the existing PR flow; UI last, per the plan.
4. Tariff sourcing runs alongside as research/procurement, not engineering, until the PO decides.

## 7. Not in this brief

No estimate beyond the plan's 4-6 months, no day-wise breakdown (to be written after sign-off), no code.
