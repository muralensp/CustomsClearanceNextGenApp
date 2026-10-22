# ADR-0044: Identity - Keycloak, `IIdentityProvider`, and Tenant/Identity Mapping

**Status:** Accepted
**Date:** 2026-10-22
**Deciders:** Product Owner (accepted each recommendation in the Phase 1 kickoff interview), Claude (design support)

## Context

ADR-0017 built an in-house login (role JWTs, 60-minute lifetime per ADR-0041) as a stand-in. It has no refresh flow,
lockout, MFA, password reset or per-tenant SSO (checklist 1.2, 1.6, 1.7, 1.8). With multiple broker firms as
customers (ADR-0043), building those features in-house is the largest and most security-critical part of User
Management for a two-person team. Hosting is undecided (ADR-0047), so the choice must not depend on a cloud.

## Decision

1. **Keycloak is the production identity provider**, self-hosted in the existing Docker Compose and later in
   whatever hosting is chosen. A managed IdP (Auth0, Entra External ID) remains a possible later change if operations
   or cost justify it.
2. **A focused `IIdentityProvider` abstraction** (create account, send invite, disable/enable, reset credentials,
   remove) in a Platform library. Keycloak is one adapter, the in-house login another. The Gateway and Api keep
   validating JWTs themselves (ADR-0019); only the trusted issuer changes.
3. **Single realm, `tenant_id` claim.** Keycloak Organizations versus a user attribute with a protocol mapper is
   chosen when this is implemented, after checking the pinned Keycloak version (not verified in this session). The
   claim name is fixed as `tenant_id`. Realm-per-tenant stays possible later; it would change only issuer resolution.
4. **Token proves identity; the app is the authority.** Credentials and MFA live in Keycloak. Tenant membership,
   roles, org scope, status and lifecycle (Provisioned > Active > Suspended > Deactivated, PRD 20.5) live in the
   app's `PlatformUser`. The JWT carries identity and tenant only, no permissions (ADR-0045).
5. **Provisioning is app-driven.** An admin creates the user in the app; the app creates the Keycloak account through
   `IIdentityProvider`. `PlatformUser` stores the Keycloak **`sub`** as the link (not email, which changes and can
   collide across tenants). **No just-in-time creation**: a token whose `sub` has no active `PlatformUser` is
   rejected. On every request the app resolves `sub` to user, tenant and status; a suspended user is blocked
   immediately. Suspension also disables the Keycloak account; if that call fails the app-side block still holds and
   the failure is retried and alerted. A mismatch between the token's tenant and the app's record is resolved in
   favour of the app and logged as a security event. Customer SSO federation is later work and still links through
   an admin-created `PlatformUser`.
6. **MFA is mandatory for every human production user** (TOTP and WebAuthn; no SMS, because of SIM-swap risk, which
   is general knowledge not verified here). Service accounts use client credentials with narrow scopes. Recovery
   is admin-mediated through the app and audited; a tenant admin is reset by a platform admin. Step-up for sensitive
   actions is a design hook (an `acr`-style claim the app can check), not built in Phase 1.
7. **Browser sessions use a backend-for-frontend at the YARP gateway** (cookie session, tokens stay server-side);
   API and service clients use bearer JWTs. Separate Keycloak clients: a confidential client for the BFF, others for
   service accounts. React + TypeScript is provisional until the UI step.
8. **The in-house login never reaches production.** It is opt-in and defaults to off. Layers: (a) startup guard in
   Gateway and Api refuses to start in Production if the in-house issuer or dev seed is enabled (as the JWT key
   guard does, ADR-0036); (b) the `/auth/login` route mapping is guarded by the same condition (Minimal API binds at
   map time); (c) the Gateway has no `/auth/login` route in production; (d) dev personas are seeded only in
   Development or CI; (e) in the Keycloak profile only the Keycloak issuer is trusted, so a token signed with the
   in-house key is rejected by Gateway and Api. Development and CI configs turn it on explicitly; nothing infers it.
9. **Keycloak outage behaviour.** JWKS are cached with a sensible refresh. Existing tokens keep validating until they
   expire; new logins fail. To be documented in the runbook.
10. **Migration.** No production users exist, so nothing is migrated. The dev personas remain dev-only. ADR-0017 is
    superseded for production, and remains the dev/CI mechanism.

## Tests

- **Identity E2E job (separate CI job, own compose profile):** real Keycloak realm imported from a checked-in export,
  real login, real token through Gateway and Api. Checks: `tenant_id` present; suspended user rejected; tenant A token
  cannot read tenant B; an MFA-less production user cannot obtain a token; in-house-signed token rejected. Each is
  shown to fail by fault injection (tampered signature, wrong issuer, expired token, missing `tenant_id`). Keycloak
  readiness uses a healthcheck with `condition: service_healthy`, never a sleep.
- **Fast layer stays infrastructure-free:** a test-signed JWT in the *same shape* as Keycloak's, with a contract test
  pinning that shape against a token captured from the real realm. ADR-0009 gets an amendment saying so.
- **Guard test:** start the Api with Production and the in-house flag on; assert it refuses to start (exit code
  captured unpiped).
- The existing Day 40 E2E (21 checks) stays on the in-house login as the safety net.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Extend the in-house login | Full control, no new service | Own every auth feature; SSO federation is a large build | Security-critical code for a two-person team |
| Managed IdP | Least operations | Per-user cost, vendor dependency, hosting undecided | Revisit later; abstraction keeps it open |
| Realm per tenant | Strongest separation, per-tenant SSO | Multi-issuer validation, heavier Keycloak | Single realm is enough now |
| Just-in-time user creation | Less code | Anyone who authenticates gets a record; weakens tenant guarantee | Contradicts app-as-authority |
| Remove login from production build | Strongest guarantee | Production runs different binaries than CI tests | Breaks "prove it on the real system" |
| Fixed Keycloak test via faked tokens only | Fast | Does not prove the real path | Kept only as the shape-pinned fast layer |

## Consequences

- Another service (Keycloak) to run; its first-boot time is a new flake risk, hence healthchecks and a separate job.
- Checklist items 1.2, 1.6, 1.7, 1.8, 6.2 change (see `docs/security/PHASE0_SECURITY_CHECKLIST.md`, Phase 1 revision).
- The per-request user lookup costs a database read (ADR-0043 sets the measurement budget).
- Single platform admin means a single-person recovery path for tenant admins (known gap).

## Related

ADR-0009 (amend), ADR-0011, ADR-0017, ADR-0019, ADR-0036, ADR-0041, ADR-0043, ADR-0045, ADR-0047; PRD 20.4, 20.5, 20.7.
