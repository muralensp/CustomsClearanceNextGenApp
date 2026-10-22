# ADR-0047: Hosting Requirements (Cloud Choice Deferred With a Deadline)

**Status:** Accepted
**Date:** 2026-10-22
**Deciders:** Product Owner (accepted the recommendation in the Phase 1 kickoff interview), Claude (design support)

## Context

PLAT-002 and ADR-0008 leave the hosting target open. A multi-tenant service with Keycloak (ADR-0043, ADR-0044) needs
TLS, a secret store, key rotation, backups and encryption at rest (checklist items 2.4, 2.6, 3.x, 4.1). India
data-protection law (DPDPA 2023) and broker contracts are likely to expect data to stay in India (not verified; see
ADR-0048 legal check). The Azure DevOps YAML has never run anywhere.

## Decision

1. **The cloud provider is not chosen now.** Backend-first Phase 1 work does not need it.
2. **Minimum requirements are fixed now** so every Phase 1 design assumes them:
   - India region for data at rest.
   - Managed PostgreSQL with point-in-time recovery, encrypted at rest (AES-256 class) and in transit
     (`SSL Mode=VerifyFull`).
   - A secrets store; the app reads production credentials from it, never from files in the image.
   - TLS 1.2+ at the edge with HSTS; TLS or mTLS between internal hops.
   - A container runtime; no provider-specific code in the application. Whether this means Kubernetes is decided with the
     cloud choice (the original Phase 0 listed Kubernetes; Phase 0 closed on Docker Compose only).
   - Backups encrypted, restore tested.
3. **Deadline.** The cloud must be chosen **before the security-hardening milestone** and before any real customer
   data exists. At that point ADR-0008's Staging and Production rows are revised and PLAT-002 closes.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Choose a cloud now | Unlocks TLS, vault, rotation items | Commitment on thin information | Not needed for backend-first work |
| Per-customer hosting (some on-prem) | Sales flexibility | Multiplies operations for a two-person team | Too costly |

## Consequences

- Checklist sections 3 and 4, and items 2.4 and 2.6, remain open until the deadline.
- Keycloak operations (backup of its database, upgrades) are specified once the host is known.
- Revisit immediately if a pilot customer names a hosting requirement.

## Related

ADR-0008, ADR-0015, ADR-0038, ADR-0043, ADR-0044, ADR-0048; PLAT-002; `docs/security/PHASE0_SECURITY_CHECKLIST.md`.
