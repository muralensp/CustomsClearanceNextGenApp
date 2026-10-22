# ADR-0048: Audit Retention and Personal Data Handling

**Status:** Accepted (retention figure provisional until the legal check)
**Date:** 2026-10-22
**Deciders:** Product Owner (accepted the recommendation; will do the legal check before production), Claude (design support)

## Context

Checklist 5.6 (retention) and 6.4 (DPDPA 2023 data-subject rights) are open. The audit trail is append-only and
database-enforced (ADR-0036), so old rows cannot simply be deleted, and data-subject erasure conflicts with immutable
rows containing names, PAN or email. With multiple tenants, each broker is a separate controller with its own
obligations. Checklist 6.2 (recipient address in the notification event payload) is the same problem.

## Decision

1. **Roles.** The platform is the data **processor**; each broker tenant is the **data fiduciary**. Erasure and
   correction requests come from the tenant admin, not from end users directly. No consumer-facing privacy portal in
   Phase 1.
2. **Retention is a per-tenant setting** with a conservative platform default of **8 years**, provisional. Nothing
   may expire before the legal minimum. The minimums for Indian customs/tax records were not verified in this
   session. The Product Owner will do the **legal check before production**.
3. **Partitioned audit tables by date**, so expiry means dropping a partition, not deleting rows.
4. **Personal data stays out of audit rows.** The immutable trail records a stable **pseudonymous subject id**;
   identifying details (name, email, PAN) live in a separate erasable table. Erasure removes the details; the audit
   trail stays intact but no longer identifies the person.
5. **Notification events carry a user id, not the address** (checklist 6.2 option a). The dispatcher resolves the
   address from the user profile at send time and stores nothing.
6. Highly Confidential fields (PAN, GSTIN, IEC) get column-level encryption designed with the Party entity (checklist
   4.2, Gate 2).

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Fixed global retention | Simple | Too short for some, wasteful for others | Per-tenant setting with floor |
| Delete rows on expiry | Simple | Conflicts with append-only grants | Partition drop instead |
| PII inside audit rows | Easy reads | Erasure impossible without breaking immutability | Pseudonymous id chosen |

## Consequences

- The audit schema needs partitioning and a subject-id design before first customer data.
- A lookup is needed to display "who" in audit views (join to the erasable table; erased subjects show as erased).
- Revisit after the legal check; the 8-year default may change in either direction.

## Related

ADR-0031, ADR-0032, ADR-0036, ADR-0043, ADR-0047; checklist 4.2, 5.6, 6.2, 6.4; DPDPA 2023.
