# ADR-0046: Data Classification - Tenant-Owned, Global, and Layered

**Status:** Accepted
**Date:** 2026-10-22
**Deciders:** Product Owner (accepted the three-way split in the Phase 1 kickoff interview), Claude (design support)

## Context

Phase 0 created tables that all predate tenancy (ADR-0043): users, audit trail, configuration and feature flags,
notifications and delivery records, rules with versions and decision traces, and reference data (countries,
currencies, UQC units; tariff to come, ADR-0042/0049). Each must be classed so the retrofit is mechanical.

## Decision

Three classes. Every table must be classified; the isolation test (ADR-0043) enforces it.

| Class | Tables (Phase 0 and planned) | Rule |
|---|---|---|
| **Tenant-owned** | users and role assignments, org tree, audit trail, notifications and delivery records, rule decision traces, tenant configuration values | `TenantId` required, indexed first, RLS on |
| **Global** | reference data and tariff | No tenant; read-only to tenants; changed only by platform staff. `IsVerified` sign-off is a platform act (ADR-0049) |
| **Layered** | configuration and feature flags, rules | Platform default plus tenant override |

Layered details:

- An explicit **`scope` marker** (`platform` or `tenant`) on every layered row, not a nullable `TenantId` with a hidden
  meaning.
- At evaluation time a tenant's rule takes precedence over the platform baseline, and the **decision trace records
  which layer** produced the outcome.
- A platform rule flagged **mandatory** cannot be overridden or shadowed by a tenant. This guards compliance rules
  against a tenant admin disabling them. (Rules-admin mutations are already audited as of 2026-10-20.)
- The three India sample rules (ADR-0029) become the first platform baseline.

Retrofit: one migration set with a **default tenant** for existing dev and seed data. The audit trail keeps its
append-only, `audit_writer` grants (ADR-0036); RLS must be compatible with that role.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Reference data per tenant | Tenant can adjust | Duplicates tariff; verification and updates become unmanageable | Every broker needs the same codes |
| Nullable `TenantId` meaning platform | One column | Hidden meaning, easy to misfilter | Explicit `scope` marker chosen |
| No tenant rules (platform rules only) | Simplest | Brokers differ in policy | Contradicts ADR-0002 intent |

## Consequences

- Rules evaluation gains a layering step; the evaluation pipeline (ADR-0026) and trace (ADR-0027) change.
- The DB role arrangement for RLS plus append-only audit needs a design note when implemented.
- Revisit when the first tenant-specific rule appears in practice.

## Related

ADR-0002, ADR-0026, ADR-0027, ADR-0029, ADR-0030, ADR-0033, ADR-0036, ADR-0043, ADR-0045, ADR-0049.
