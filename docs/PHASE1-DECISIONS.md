# Phase 1 Decisions Log and Scope Note

Record of the Product Owner interview of 2026-10-22 (`/grill-me` on `docs/PHASE1-KICKOFF-BRIEF.md`). Every decision
below was a Claude recommendation that the Product Owner accepted; the ADR holds the reasoning. Nothing here is built.

## Decisions

| # | Decision | ADR |
|---|---|---|
| 1 | Multi-tenant (selling to multiple broker firms); `TenantId` on every tenant-owned row | 0043 |
| 2 | Isolation: EF global filters plus PostgreSQL RLS; tenant from the `tenant_id` JWT claim; fail closed | 0043 |
| 3 | Identity: Keycloak behind `IIdentityProvider`; in-house login kept for dev and CI only | 0044 |
| 4 | Single Keycloak realm, `tenant_id` claim; Organizations vs attribute chosen at implementation | 0044 |
| 5 | Tenant = Enterprise; legal entities are nodes inside; org visibility is an authorization rule | 0043 |
| 6 | Hosting: requirements ADR now; cloud chosen before the security-hardening milestone | 0047 |
| 7 | Permission-based RBAC, org-scoped assignments, custom tenant roles later; SoD hook only | 0045 |
| 8 | Browser sessions via gateway BFF (cookie); bearer JWT for API and service clients; React + TypeScript provisional | 0044 |
| 9 | Direction: file with ICEGATE eventually, advise-only first; obtain the ICES specification | 0049 |
| 10 | Tariff: PO signs off at first, live filing blocked for PO-only rows; official sources base; ceiling about INR 1,00,000 per year | 0049 |
| 11 | Audit retention per tenant, 8-year provisional default; processor/fiduciary roles; PO does the legal check before production | 0048 |
| 12 | Notification in Phase 1 is tenant-aware and production-safe only | below |
| 13 | Data classes: tenant-owned, global, layered (`scope` marker); mandatory platform rules | 0046 |
| 14 | Build order: docs, tenancy foundation on the in-house login, Keycloak, users/org/permissions, notification hardening, BFF, UI | below |
| 15 | Keycloak tests in a separate identity E2E job plus an infra-free shape-pinned test; ADR-0009 amended | 0044, 0009 |
| 16 | In-house login opt-in, fail-closed, startup guard, proven by two tests | 0044 |
| 17 | App-driven provisioning, linked by `sub`, no just-in-time creation | 0044 |
| 18 | Tenants created by platform staff only; transactional; suspension immediate; offboarding designed, deferred | 0043 |
| 19 | MFA mandatory for all human production users; no SMS | 0044 |
| 20 | Tenant in event envelope, `SET LOCAL`, dead-letter on missing tenant | 0043 |
| 21 | No user-lookup cache at first; k6 re-run with about 20% p95 trigger (proposed, not measured) | 0043 |

## Phase 1 scope note

- **In scope:** tenancy foundation, Keycloak integration, User Management, Organization, authorization,
  Notification hardening (tenant-aware, per-user preferences, templates, retry, recipient-address fix), audit
  retention design, configuration layering, gateway BFF, UI last. Tariff sourcing runs alongside as research and
  procurement.
- **Deferred, deliberately:** task management, SLAs, escalation, collaboration and work queues (PRD ch. 16). They need
  a real workflow to be tested against and Phase 1 has none; they move to the first phase that does. This is an
  explicit scope change from the plan, not forgotten work.
- **Corrections to the brief:** the rules-admin audit gap and the script `trap` ordering were closed in the Phase 0
  follow-ups (CLAUDE.md). Only restarting `docs/WORK-ITEMS.md` remained and is done in this branch.

## Build order

1. This documentation branch and `WORK-ITEMS.md`.
2. Tenancy foundation on the in-house login (`tenant_id` claim, resolver, columns, filters, RLS, default-tenant
   migration, isolation tests); re-run E2E, contract suite and k6.
3. `IIdentityProvider` and Keycloak, identity E2E job, production switch-off of the in-house login.
4. User management, org tree, permissions.
5. Notification hardening, then gateway BFF, then UI.

Each step passes Gate 1 and Gate 2 and goes through the PR flow. No code starts without an explicit instruction.

## Open items on the Product Owner

- Legal check on retention minimums and the processor/fiduciary split, before production.
- Obtain the official ICES specification and onboarding rules.
- Collect tariff data vendor quotes against the ceiling.
- Choose the cloud before the security-hardening milestone.
- Confirm facts marked unverified in the ADRs (Keycloak Organizations support in the pinned version; ICEGATE signing rules).

## Not grilled

Billing and plans, UI design, the exact permission catalogue, and the jurisdiction and target-authority questions
(India only; ICEGATE unconfirmed).
