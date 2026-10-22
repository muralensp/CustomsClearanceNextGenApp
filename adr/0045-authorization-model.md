# ADR-0045: Authorization Model - Permissions, Roles, and Org Scope

**Status:** Accepted
**Date:** 2026-10-22
**Deciders:** Product Owner (accepted the recommendation in the Phase 1 kickoff interview), Claude (design support)

## Context

Authorization today is four code-defined roles (Broker, Supervisor, ComplianceOfficer, Admin) enforced by JWT role
policies (ADR-0011, ADR-0017). Multi-tenancy (ADR-0043), the org tree and Keycloak (ADR-0044) change what a role
means: brokers differ in who may file or approve, and "configure who can do what without a release" matches the
configuration-driven philosophy of ADR-0002.

## Decision

1. **Permission-based RBAC.** The code defines fine-grained permissions (for example `job.file`, `rule.publish`,
   `user.suspend`). Policies check permissions, not role names.
2. **Roles are bundles of permissions.** System-seeded default roles map onto today's four roles, so existing
   policies and the Day 40 E2E checks keep working. **Tenant-editable custom roles** come in a later step, once the
   permission catalogue has settled.
3. **Assignments are `(user, role, org-node scope)`**, for example Supervisor at Branch Chennai. Org-unit visibility
   is an authorization rule on top of `TenantId`, not a second isolation mechanism (ADR-0043).
4. **Permissions are resolved by the app on each request**, not carried in the JWT. A revocation takes effect
   immediately instead of at token expiry (60 minutes today).
5. **Separation of duties.** The data model includes an exclusion/conflict structure from the first version.
   Enforcing specific rules (preparer is not approver) is added per workflow when each workflow is built. Phase 1 has
   no filing workflows. This was the Product Owner's unanswered sub-question; the default was applied.
6. **Role and permission changes are audited** through the audit trail (ADR-0032).
7. **Platform admin** is a role outside any tenant (reference data, platform rules, tenant creation), audited under
   the `platform` tenant.
8. **Mandatory platform rules** (ADR-0046) cannot be overridden or shadowed by a tenant admin.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Fixed roles plus org scope | Simplest | Every new role needs a release | Does not meet tenant variability |
| Full ABAC / policy engine (OPA) | Most flexible | Heavy for this stage | Revisit if permissions plus scope prove insufficient |

## Consequences

- A permission catalogue becomes a maintained artefact, versioned with the code.
- Every endpoint's policy moves from role names to permissions (done incrementally, E2E must stay green).
- Resolving permissions per request adds a read; measured per ADR-0043.
- Revisit when custom roles are built, or when the first filing workflow needs SoD rules.

## Related

ADR-0002, ADR-0011, ADR-0017, ADR-0032, ADR-0043, ADR-0044, ADR-0046; PRD 20.
