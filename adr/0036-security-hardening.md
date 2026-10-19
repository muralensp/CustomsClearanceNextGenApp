# ADR-0036: Security Hardening (Day 41)

**Status:** Accepted
**Date:** 2026-10-19
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 9, Day 41: "Review auth flows, secrets management, encryption at rest/in transit against
Phase -1 security standards"; deliverable "Security checklist passed". The standard is
`Day10_Security_Audit_Standards_v1.0.docx` (ADR-0004). Five carry-forwards were open: database-level audit
immutability (ADR-0032), the duplicate `X-Correlation-Id` response header (ADR-0032), JWT key handling
(the placeholder key is in a now-public repository), the NU1902 advisory (ADR-0022), and the recipient
email inside the notification event (ADR-0031). The full item-by-item review is
`docs/security/PHASE0_SECURITY_CHECKLIST.md`.

## Decision

1. **Audit immutability by a restricted database role, not a trigger.** The runtime connection
   (`ConnectionStrings:AuditDb`) logs in as `audit_writer`, which is granted only `SELECT` and `INSERT` on
   `"AuditEvents"` (`REVOKE ALL` then `GRANT SELECT, INSERT`: plain SQL, portable to SQL Server, so
   ADR-0001 holds; a trigger would not be). The table, the role and the grants are created at bootstrap
   over a separate owner connection, `ConnectionStrings:AuditAdminDb`. The module is still enabled iff
   `AuditDb` is present; `AuditAdminDb` is optional (absent: old behaviour plus a warning). The
   application-level `EnsureAppendOnly` guard stays. Bootstrap stays Development-only as before. The
   grant statements live in `Platform.Audit.Persistence` (`AuditStoreGrants`, identifiers validated, not
   escaped); `CREATE ROLE` is PostgreSQL-specific, so it sits in the Api host's `AuditModule`, which is
   already provider-aware, and is described as dev provisioning (production: IaC/DBA).
2. **Correlation header set once.** `CorrelationIdMiddleware` writes the response header in an
   `OnStarting` callback using the indexer (replace), so a copy appended by YARP from the Api's response
   is overwritten. Fixed in the shared middleware rather than special-casing the Gateway, because the
   Gateway and Api run the same middleware and any other proxy would hit the same issue.
3. **JWT key fail-fast outside Development.** `JwtKeyValidator.EnsureAcceptable` runs from
   `AddCustomsClearanceAuthentication` and `AddCustomsClearanceTokenIssuance` (new optional
   `IHostEnvironment` parameter; no environment passed counts as "not Development", the safe default). It
   rejects keys containing `LOCAL-DEV-ONLY`, `PLACEHOLDER` or `NEVER-USE-IN-PRODUCTION`, and keys under
   32 UTF-8 bytes. Development is unchanged. `docker-compose.yml` takes the key from `${JWT_KEY:-<dev
   placeholder>}` for both services. Because the validator keys off the environment name, the Dockerfiles
   no longer bake in `ASPNETCORE_ENVIRONMENT=Development` (now `Production`; compose sets Development
   explicitly), otherwise an image run without an override would have been a Development host.
4. **NU1902 fixed by upgrading, not suppressing.** OpenTelemetry packages moved 1.9.0 to 1.15.x as one set
   (Extensions.Hosting 1.15.3, Instrumentation.AspNetCore 1.15.2, Instrumentation.Http 1.15.1,
   Exporter.Prometheus.AspNetCore 1.15.3-beta.1, Exporter.OpenTelemetryProtocol 1.15.3). The advisory is
   fixed in 1.15.3; 1.11.2 was tried first and is still flagged. This supersedes ADR-0022's decision to
   leave the package alone (its Prometheus-beta compatibility concern did not materialise).
5. **Notification PII: assessed, only the small safe part fixed.** The address cannot leave the payload
   without a user-profile lookup (a feature, excluded). Fixed: SMTP failure text that echoes the address is
   redacted before it is logged or stored on the delivery row, and the exception object is not logged.
   Left as an accepted gap with options (id-only event plus lookup at send time, field encryption, or
   clearing after delivery) in the checklist, item 6.2.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Trigger raising on UPDATE/DELETE | Blocks even the table owner's casual statements | Provider-specific PL/pgSQL; ADR-0001 forbids; owner can drop it | Product owner decided on a restricted role |
| Create the role in a postgres init script | No role DDL in app code | Runs only on a fresh volume; existing dev volumes would break | Bootstrap creates it idempotently instead |
| One connection for bootstrap and runtime | Simpler config | Runtime would hold the owner rights, defeating the role | Separate `AuditAdminDb` |
| Reject the key only when it equals the exact placeholder | Simple | A lightly edited placeholder passes | Marker match plus minimum length |
| Validate the key in the Gateway/Api `Program.cs` only | No signature change | Duplicated, easy to skip in a new host | Shared `JwtKeyValidator` |
| Suppress NU1902 with a justification | No dependency churn | The exporter stays vulnerable | Patched version works |
| Strip the response header in a YARP transform | Targeted | Gateway-only, relies on config | Fix in the middleware, covers every hop |

## Consequences

- The application and any injection through it can no longer alter or remove audit rows. **Residual:** the
  table owner (a superuser in the dev stack) and any DBA can; tamper-evidence, retention and partitioning
  remain open (checklist 5.3, 5.6).
- Non-Development production hosts do no schema bootstrap, so the audit table, the `audit_writer` role and
  the grants must be provisioned by IaC/DBA with the same statements. `AuditStoreGrants.BuildStatements`
  is the reference.
- The Gateway and Api now refuse to start outside Development with the committed key. Any new host must
  pass its environment to the two auth registrations (there is no compile-time reminder; the default is
  the safe one).
- Running an image directly (not through compose) now starts in Production and fails without a real key,
  which is intended.
- `docker compose` for existing dev volumes: the first start after this change creates `audit_writer` and
  re-applies grants; no manual step.
- Revisit when the hosting target and IdP are chosen: TLS everywhere, secrets store, MFA, token lifetime,
  `/metrics` exposure, security headers, and the CI gates for secrets scanning and vulnerable packages
  (all listed in the checklist).

## Related

[[ADR-0001]] (portability), [[ADR-0004]] (the standard), [[ADR-0011]], [[ADR-0017]], [[ADR-0019]],
[[ADR-0022]] (NU1902 note superseded), [[ADR-0031]] (PII in the payload), [[ADR-0032]] (carry-forwards
closed here), [[ADR-0035]] (module structure).
