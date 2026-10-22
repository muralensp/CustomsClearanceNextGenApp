# ADR-0043: Multi-Tenancy and Data Isolation

**Status:** Accepted
**Date:** 2026-10-22
**Deciders:** Product Owner (accepted each recommendation in the Phase 1 kickoff interview), Claude (design support)

## Context

The Phase 1 kickoff brief (`docs/PHASE1-KICKOFF-BRIEF.md`, decision 1) names tenancy as the decision that blocks all
others: no tenant concept exists in `src/` today, and retrofitting a tenant key after User, Organization and Audit
tables exist is the most expensive mistake available in this phase. PRD 20.10 allows single-tenant or multi-tenant.
The Product Owner plans to **sell to multiple broker firms**, so isolation is a requirement, not a hedge.

## Decision

1. **Multi-tenant, shared schema.** Every tenant-owned table carries a non-null `TenantId`. Classification of each
   Phase 0 table is in ADR-0046.
2. **Tenant = Enterprise**, the root of the PRD 21.5 hierarchy (Enterprise > Country > Region > Branch > Department
   > Team). Legal entities are nodes inside the tenant and carry the broker licence and tax identifiers. Visibility
   by branch or department is an *authorization rule* inside the tenant (ADR-0045), never a second isolation
   mechanism. If a holding-company customer appears, an optional `TenantGroup` above tenant is additive.
3. **Two enforcement layers.** EF Core global query filters are the portable primary mechanism. PostgreSQL
   Row-Level Security is the database-enforced backstop, reading a session setting (`app.tenant_id`). RLS is
   PostgreSQL-specific, so on the SQL Server path it would have to be rebuilt; the EF filters keep that path open
   (ADR-0001).
4. **Fail closed.** Code that opens a connection without a tenant context sees **zero rows**, not all rows.
5. **Tenant resolution** is a port (`ITenantContext`). In HTTP it comes from the `tenant_id` JWT claim (ADR-0044); in
   a consumer it comes from the event envelope. A large customer who demands a dedicated database can later be
   served by swapping the resolver and connection factory, not the domain code.
6. **Session setting via `SET LOCAL`.** The tenant is set with `set_config('app.tenant_id', ..., true)` inside the
   transaction, owned by the unit-of-work layer (ADR-0012). Use-case code never touches it. It disappears at commit or
   rollback, so a pooled Npgsql connection cannot leak one tenant's setting into the next request.
7. **Events carry the tenant.** `DomainEventEnvelope` gains a mandatory header `TenantId` (value `platform` reserved
   for platform-level events). The publisher fills it from the ambient context, and publishing without one fails.
   The envelope is versioned and documented in `asyncapi/` with the first real event. A consumer establishes the
   tenant context from the envelope before any database work; a message with a missing or unknown tenant goes to the
   dead-letter queue and raises an alert. It is never processed under a default tenant. Only `platform` events cross
   tenants.
8. **Observability.** Tenant ID goes on log scopes and as a trace tag, **never as a Prometheus label** (cardinality,
   and customer identity in metrics).
9. **Performance.** `TenantId` is the leading column of composite indexes. The per-request user lookup is not cached
   at first (revocation must be immediate, ADR-0044). After the tenancy foundation, the Day 42 k6 scenarios are
   re-run; a regression of about 20% in p95 latency on the login-free read endpoints triggers investigation. The 20% is
   a proposed budget, not a measured one.
10. **Retrofit** is one migration set with a **default tenant** for existing dev and seed data.
11. **Tenant lifecycle.** States: Provisioning, Active, Suspended, Offboarded. Tenants are **created by platform staff
    only** (a platform admin role outside any tenant) through one transactional use case: tenant row, root Enterprise
    node, first legal-entity record, first tenant admin, and the invite via `IIdentityProvider`. It either fully
    succeeds or leaves nothing behind. There is no self-service sign-up endpoint, so no unauthenticated write path
    exists in the Api. **Suspension** is applied by the app and blocks logins and all API calls except a read-only
    export for the tenant admin; it does not depend on Keycloak. **Offboarding** is designed as a state now and
    built as a documented procedure later: export, retention hold until the legal minimum (ADR-0048), then deletion
    by partition, with a second approver.

## Required proofs (fault injection, not just passing)

- An isolation test that **fails** when any table classified tenant-owned lacks `TenantId` or an RLS policy.
- A pooled-connection test: tenant A then tenant B on one connection; B never sees A's rows.
- A consumer test: an envelope without a tenant is dead-lettered, not processed.
- A tenant-creation test that injects a failure halfway and asserts nothing is left behind.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Single-tenant per deployment | Simplest isolation | One stack per customer; does not fit multi-firm sales | Commercial plan is multi-firm |
| Schema per tenant | Stronger separation | Migrations run N times; provider-specific handling | Conflicts with keeping SQL Server open |
| Database per tenant | Strongest isolation, easy per-customer backup | Operations cost grows per customer | Kept as an upgrade path via the resolver port |
| Tenant = legal entity | Matches licence boundary | Firms run several entities as one operation | Breaks cross-entity queues and reporting |
| Self-service tenant sign-up | Scales | Abuse control, billing, name-claim risk | Too much for Phase 1; regulated product should know its customers |

## Consequences

- Every new table needs a classification (ADR-0046) and, if tenant-owned, a column, index and policy.
- The envelope change is breaking for existing consumers (Notification, Audit); they are updated together.
- The platform admin is a single person at first (Product Owner); recorded as a known single-approver gap.
- Revisit if a customer contractually requires a dedicated database, or if p95 regression exceeds the budget.

## Related

ADR-0001, ADR-0012, ADR-0017, ADR-0018, ADR-0040, ADR-0044, ADR-0045, ADR-0046, ADR-0048; PRD 20.10, 21.5, 21.6;
`docs/PHASE1-KICKOFF-BRIEF.md`.
