# Phase 0 Security Checklist (Day 41)

Source standards: `Day10_Security_Audit_Standards_v1.0.docx` (via ADR-0004), Gate 1 and Gate 2 security
items (`docs/quality-gates/`). Decisions and rationale: `adr/0036-security-hardening.md`. Run log:
`Phase-1_Architecture_Engineering_Prep/Day41_Execution_Log.md`.

Status key: **PASS** (meets the standard, evidence given) - **FIXED** (changed on Day 41) -
**ACCEPTED-GAP** (dev-only or deliberately deferred, with reason) - **OPEN** (needs a product-owner decision).

Evidence marked "code" was established by reading the source, not by running it; "live" was run on the
real compose stack (slot 1) on 2026-10-18/19.

## 1. Authentication (Day 10 items 1, 15)

| # | Item | Status | Evidence / note |
|---|---|---|---|
| 1.1 | Login issues role-based JWT | PASS | `POST /auth/login`, `src/Api/Modules/IdentityModule.cs`; Day 23 live matrix, re-exercised by E2E checks 2-3 (live). |
| 1.2 | Unknown user and wrong password are indistinguishable | PASS (response) / ACCEPTED-GAP (timing) | Both return 401 with no body (code). The unknown-user path skips password hashing, so response time can reveal a valid username. Low risk for a dev stand-in; fix with the real IdP. |
| 1.3 | Password hashing | PASS | ASP.NET Core `PasswordHasher<T>` (PBKDF2, per-user salt) in login and `IdentitySeeder` (code). |
| 1.4 | JWT validated at the Gateway AND the Api | PASS | Issuer, audience, signature and lifetime validated in both (`AddCustomsClearanceAuthentication`); ADR-0019 matrix, E2E check 6 (live). |
| 1.5 | Role policies | PASS | `PlatformPolicies`/`PlatformRoles`; `RequireAdmin` etc. enforced on `/api/rules`, `/api/config`, `/api/audit`, `/api/notifications` (code; 401/403 matrices from Days 34-38). |
| 1.6 | Token expiry | FIXED (partly; ADR-0041) / ACCEPTED-GAP (refresh) | Lifetime is now `Jwt:AccessTokenMinutes` (default **60**, was a hard-coded 8 h; accepted range 1 to 1440, checked at start-up). Live: a login returns `expiresAtUtc` 60.0 minutes ahead; 28 unit tests in `Platform.Security.Tests`. Day 10 says 15 min **plus refresh**; there is still no refresh-token flow, so a user re-logs in every hour. Set the value to 15 and users re-login every 15 minutes; a proper fix needs the real IdP (OIDC). `ClockSkew` is 30 s (PASS). |
| 1.7 | Brute-force / lockout | ACCEPTED-GAP (decided Day 45: documented, not built) | No lockout or per-account throttle on `/auth/login`. Only the Gateway's global 100 req/10 s/IP limiter applies. Day 10 item 15 requires lockout after 5 failures if local accounts exist; they exist only as dev personas. Deliberately not built now because it needs design decisions (where failures are counted across Api instances, how and when an account unlocks, and how to stop an attacker locking real users out) that belong with the real identity provider. Needed before any non-dev exposure. |
| 1.8 | MFA for Supervisor/Compliance/Admin | OPEN | Not implemented. Day 10 assumes an external IdP (no local accounts); ADR-0017 built an in-house stand-in instead. Decision needed when the IdP is chosen. |
| 1.9 | Dev personas/passwords only in Development | PASS | Seeding runs only inside `if (app.Environment.IsDevelopment())` in `Program.cs` (code); passwords are in `IdentitySeeder.cs` (public repo, dev-only). |
| 1.10 | Service-to-service auth | ACCEPTED-GAP | Gateway to Api is plain HTTP on the internal compose network; the Api re-validates the user JWT. No service token. Single deployable (ADR-0003), so no other internal hop exists yet. |

## 2. Secrets management (Day 10 item 5 + Gate 1 "no secrets in source")

| # | Item | Status | Evidence / note |
|---|---|---|---|
| 2.1 | Dev JWT key committed (`LOCAL-DEV-ONLY-PLACEHOLDER-...`) in `src/Gateway/appsettings.json` and `src/Api/appsettings.Development.json`; repo is public | ACCEPTED-GAP (dev) + FIXED (guard) | Anyone can mint a valid token with this key, so it must never validate tokens outside a developer machine. New `JwtKeyValidator`: outside Development, startup throws if the key contains the placeholder markers or is under 32 bytes. Live: gateway image with `ASPNETCORE_ENVIRONMENT=Production` and no key exits with `Jwt:Key is the committed development placeholder...`; with `Jwt__Key=short` exits with the length error; with a 51-char key it starts and listens. Api in Production with no key fails with the existing "Jwt:Key is required". 9 unit tests. |
| 2.2 | Hosting environment defaults | FIXED | Both Dockerfiles had `ENV ASPNETCORE_ENVIRONMENT=Development` baked in, so an image run without an override would have used the placeholder key, seeded dev users and Swagger. Default is now `Production`; `docker-compose.yml` sets Development explicitly (it already did). Smoke test and E2E pass on the changed images. |
| 2.3 | Key supplied from environment | FIXED | `docker-compose.yml`: `Jwt__Key=${JWT_KEY:-<dev placeholder>}` on `api` and `gateway`; `docker compose config` with `JWT_KEY` set shows the override on both. |
| 2.4 | DB password `devlocalonly`, `audit_writer` password, RabbitMQ `guest/guest` committed | ACCEPTED-GAP (dev) | Used only in `docker-compose.yml` and `appsettings.Development.json`. `appsettings.Development.json` is not loaded outside Development, so a non-Development host has no connection strings: modules stay disabled (it does not fall back to the dev password). Production credentials must come from a secrets store (OPEN until hosting is chosen, ADR-0008). |
| 2.5 | Non-Development host silently running with dev credentials | PASS | Key: guarded (2.1). DB: not loaded (2.4). Seed users: Development-only (1.9). Image default: Production (2.2). |
| 2.6 | Key rotation / vault | OPEN | No vault, no rotation process (Day 10: 90 days). Deferred with hosting. |
| 2.7 | Secrets scanning in CI (Day 10 item 16) | FIXED (Day 43, ADR-0038) | `build/scripts/secret-scan.sh` (gitleaks, full history) is a CI job. Passed locally on 32 commits; not yet run on GitHub. |
| 2.8 | `.env` and local settings ignored | PASS | `.gitignore` lists `.env`, `*.local.json`. |

## 3. Encryption in transit (Day 10 item 5: TLS 1.2+ on every hop)

| # | Item | Status | Evidence / note |
|---|---|---|---|
| 3.1 | Client to Gateway | ACCEPTED-GAP (dev) | Dev compose serves plain HTTP on 8080; no `UseHttps`/HSTS configured anywhere (code). **Production requirement:** TLS 1.2+ (1.3 preferred) terminated at the edge (load balancer or Gateway), HSTS on. Not built here by instruction; depends on the hosting decision. |
| 3.2 | Gateway to Api | ACCEPTED-GAP | HTTP inside the compose network. Production: TLS or mesh mTLS per Day 10 item 4. |
| 3.3 | Api to PostgreSQL | ACCEPTED-GAP | Npgsql connection strings have no `SSL Mode`; the postgres container has TLS off. Production: `SSL Mode=VerifyFull` with a trusted CA and server TLS enabled. |
| 3.4 | Api to RabbitMQ | ACCEPTED-GAP | AMQP on 5672 without TLS, `guest` account. Production: AMQPS (5671), a dedicated user, no `guest`. The event payload carries the recipient email (see 6.2), which makes this hop matter. |
| 3.5 | SMTP | ACCEPTED-GAP | `EnableSsl` is off for Mailpit. Production relay must use STARTTLS/TLS. |
| 3.6 | OTLP to Tempo | ACCEPTED-GAP | Plain gRPC inside the compose network. |

## 4. Encryption at rest (Day 10 item 5: AES-256 DB/storage)

| # | Item | Status | Evidence / note |
|---|---|---|---|
| 4.1 | PostgreSQL volume | ACCEPTED-GAP (dev) | Docker named volume, unencrypted. **Production requirement:** managed-database or disk encryption (AES-256 TDE equivalent) plus encrypted backups. Host-dependent; not built. |
| 4.2 | Column-level encryption of Highly Confidential fields (PAN/GSTIN/IEC) | PASS (n/a today) | No such fields exist yet (all 17 contexts are empty scaffolding). Must be designed in Phase 1 with the Party entity (Gate 2 item). |
| 4.3 | Passwords | PASS | Stored as PBKDF2 hashes only. |
| 4.4 | Notification email address stored in plain text | ACCEPTED-GAP | `Notification.EmailAddress` (personal data). See 6.2. |
| 4.5 | Grafana/Prometheus/Loki/Tempo data | ACCEPTED-GAP | Unencrypted local volumes; logs must not hold Confidential data (6.1). |

## 5. Audit (Day 10 items 7-9, 17; ADR-0032)

| # | Item | Status | Evidence / note |
|---|---|---|---|
| 5.1 | Append-only at the application level | PASS | `AuditDbContext.EnsureAppendOnly` (kept), 19 unit tests in `Platform.Audit.Persistence.Tests`. |
| 5.2 | Append-only at the database level ("restricted DB role", Day 10 item 9) | FIXED | Runtime connection uses role `audit_writer`; owner bootstrap grants only SELECT and INSERT on `"AuditEvents"` (`AuditStoreGrants`, standard GRANT/REVOKE; no trigger). Live as `audit_writer` via psql: SELECT works, INSERT works, UPDATE/DELETE/TRUNCATE all `permission denied for table AuditEvents`; the same role is also denied on `"Users"`. The app still works end to end (E2E 20/20, `smoke-test.sh` exit 0). Also asserted in `smoke-test.sh`. |
| 5.3 | Residual: table owner / superuser can still change rows | ACCEPTED-GAP | `\dp` shows the owner `customsclearance` holds `arwdDxt`, and in dev it is also a superuser. The restricted role protects against the application and SQL injection through it, not against a privileged DBA or a stolen owner credential. In production the owner/migration credential must not be held by the running app and DBA access must itself be audited. Tamper-evidence (hash chaining) not built. |
| 5.4 | Dev fallback without `AuditAdminDb` | ACCEPTED-GAP | If `ConnectionStrings:AuditAdminDb` is absent in Development, the table is bootstrapped over the runtime connection and no grants apply; a warning is logged. Non-Development hosts do no bootstrap at all (so table, role and grants must be provisioned by IaC/DBA). |
| 5.5 | Audit and change not atomic | ACCEPTED-GAP | Unchanged from ADR-0032 (sequential commits). |
| 5.6 | Retention (5 years) and monthly partitioning | OPEN | Not implemented; retention duration still needs product-owner confirmation (Day 10 item 18). |
| 5.7 | Every audit-worthy action audited | ACCEPTED-GAP | Config changes, rules admin mutations (create/update/deactivate/add version, closed 2026-10-20, E2E-proven) and `/diagnostics/secure`. Gate 2 item for each new mutation. |
| 5.8 | Authorization decisions on Confidential+ fields audited | OPEN | No Confidential fields yet. |
| 5.9 | Correlation ID on audit rows | PASS | E2E check 15 (live): audit row carries the request's correlation ID. |

## 6. Data protection and logging (Day 10 items 6, 11, 12; Gate 2)

| # | Item | Status | Evidence / note |
|---|---|---|---|
| 6.1 | Structured logs with correlation ID, no Confidential values | PASS | JSON console logging; correlation scope on every line (Days 16/26). Notification logs carry the recipient username, never the email (code). `EnableSensitiveDataLogging` is not used anywhere (grep). **Caveat:** EF logs SQL text at Information level in Development, which includes parameter placeholders but not values. |
| 6.2 | Recipient email in the `NotificationRequested` event payload (ADR-0031) | ACCEPTED-GAP (mitigated) | Removing it needs a user-profile/address lookup, which is a feature (out of scope). Mitigations done: the address is never logged; SMTP error text that echoes it is now redacted before it is logged or stored on the delivery row (`NotificationDispatcher.RedactAddress`, unit test), and the exception object is no longer logged. The address is not returned by the notification API (code). Remaining exposure: it sits in the RabbitMQ message and queue (no TLS, 3.4) and in `Notifications.EmailAddress`. **Options** (decision needed): (a) event carries only a user id, dispatcher resolves the address from a profile at send time and nothing is stored; (b) keep the payload but encrypt that field; (c) null `EmailAddress` after successful delivery. (a) is the right end state and belongs with the Party/User-profile work. |
| 6.3 | Masking at the API layer | PASS (n/a today) | No Confidential fields served yet. |
| 6.4 | DPDPA 2023 data-subject rights | OPEN | Needs a decision before Phase 3 (Day 10 item 18). |

## 7. API and edge security (Day 10 items 3, 14, 16)

| # | Item | Status | Evidence / note |
|---|---|---|---|
| 7.1 | Gateway is the only published edge | PASS | `api` publishes no host port in `docker-compose.yml`. |
| 7.2 | Rate limiting | PASS (dev values) | Global fixed window, 100 req per 10 s per IP (Day 22). Not tuned. Another workstream owns changes to it. |
| 7.3 | Security headers (HSTS, nosniff, X-Frame-Options, Referrer-Policy, CSP) | OPEN | None set at the Gateway (grep). Small to add, but needs TLS first for HSTS and a UI choice for CSP. |
| 7.4 | Duplicate `X-Correlation-Id` response header | FIXED | `CorrelationIdMiddleware` now sets the response header at response start with the indexer, so the proxied copy is replaced. Live: `curl -i` through the Gateway shows one header; E2E check 7 now asserts the count is exactly 1. Unit test simulates a downstream that appends the header. Not re-demonstrated in its "before" state on this branch (the duplicate was observed on Day 38). |
| 7.5 | `/metrics` reachable through the Gateway | FIXED (Day 45, ADR-0041) | The Gateway's `/metrics` was mapped on the public port without authentication (found by reading the code on Day 41; the before-state was not re-run live). Now it is served only on an internal port (9100, never published); on the public port every `/metrics` request is a 404. Live: `/metrics`, `/Metrics`, `/metrics/`, `/metrics?x=1`, a POST and a request with a spoofed `Host: localhost:9100` header all return 404; port 9100 is not reachable from the host; Prometheus still scrapes `gateway:9100` and `api:8080` (both `up`). The check is on the connection's local port, not the Host header. The Api host's own `/metrics` stays internal because the Api is unpublished. |
| 7.6 | Swagger exposure | PASS | Mapped only in Development (`Program.cs`); images now default to Production (2.2). |
| 7.7 | Dev observability/messaging consoles open | ACCEPTED-GAP (dev) | Grafana anonymous Admin, Prometheus, Loki, Tempo, Mailpit UI and RabbitMQ management are published on host ports in the dev compose. Must not exist in any shared environment. |
| 7.8 | Input validation / error leakage | PASS | `ExceptionHandlingMiddleware` returns the standard error body with a correlation ID, no stack traces (Day 16/34, unit-tested). |
| 7.9 | Dependency (SCA) scanning | FIXED (finding) / OPEN (gate) | NU1902 on `OpenTelemetry.Exporter.OpenTelemetryProtocol` (GHSA-4625-4j76-fww9, fixed in 1.15.3) cleared by moving the OpenTelemetry packages 1.9.0 to 1.15.x; build is now 0 warnings. 1.11.2 was tested first and was still flagged. Day 43 (ADR-0038) added `build/scripts/dependency-scan.sh` as a CI job that fails on High/Critical advisories; on the merged tree it reports 0 blocking and 0 warnings. Not yet run on GitHub, and it is not enforced until branch protection is enabled. |
| 7.10 | SAST | PASS | SonarAnalyzer.CSharp in every build (ADR-0007); build 0 warnings. |
| 7.11 | DAST, penetration test | OPEN | Scheduled pre-Phase 8 per Day 10. |
| 7.12 | Digital signature for ICEGATE submissions | OPEN | Phase 8. |

## Summary

- FIXED on Day 41: audit DB-level immutability, duplicate correlation header, JWT key fail-fast,
  Dockerfile environment default, NU1902, email redaction in notification errors/logs.
- Needs a decision: MFA/IdP (1.8), notification PII design (6.2), security headers (7.3), retention (5.6),
  DPDPA handling (6.4). Decided on Day 45 and no longer open: `/metrics` exposure (7.5, fixed), token lifetime
  (1.6, now a 60-minute setting; refresh still needs the IdP), login lockout (1.7, documented not built).
- CI changes, made afterwards on Day 43: secrets scanning (2.7) and failing the build on a vulnerable
  package (7.9).
- Everything under 3 and 4 is a production requirement that depends on the hosting decision.

## Phase 1 revision (2026-10-22, documentation only; nothing below is built yet)

Decisions: ADR-0043 (tenancy), ADR-0044 (identity), ADR-0045 (authorization), ADR-0047 (hosting), ADR-0048
(audit retention and personal data). "Planned" means designed and accepted, not implemented or verified.

| # | Phase 1 status | What changes |
|---|---|---|
| 1.2 | Planned closure | Keycloak handles credentials; timing leak applies only to the dev login, which is off in production (ADR-0044 decision 8). |
| 1.6 | Planned closure | Refresh and token lifetime move to Keycloak. `Jwt:AccessTokenMinutes` stays for the dev login. |
| 1.7 | Planned closure | Lockout and throttling come from Keycloak brute-force detection. Still an accepted gap until then. |
| 1.8 | Decided | MFA mandatory for every human production user, no SMS, admin-mediated recovery (ADR-0044). Open until built. |
| 1.9 | Extend | Add: dev personas seeded only in Development or CI; production startup guard (new test). |
| 1.10 | Unchanged | Service-to-service auth still a gap; Keycloak service accounts cover future machine clients. |
| 2.1 | Extend | Add: in-house signing key must not be trusted in the Keycloak profile; test that an in-house-signed token is rejected. |
| 2.4, 2.6 | Open | Wait for the hosting choice; deadline set in ADR-0047. |
| 3.x, 4.1 | Open | Production requirements fixed in ADR-0047; unbuilt until the cloud is chosen. |
| 4.2 | Open | Column-level encryption designed with the Party entity (ADR-0048 decision 6). |
| 4.4, 6.2 | Decided | Events carry a user id; address resolved at send time, nothing stored (ADR-0048 decision 5). |
| 5.6 | Decided (provisional) | Per-tenant retention, 8-year default, date partitions; legal check by the Product Owner before production. |
| 5.8 | Open | Still waits for the first Confidential fields. |
| 6.4 | Decided | Platform is processor, tenant is fiduciary; requests come from the tenant admin (ADR-0048). Legal check pending. |
| 7.3 | Open | Needs TLS (hosting) and the UI choice. |
| 7.12 | Open | Phase 8; ICES specification action added to Phase 1 (ADR-0049). |
| NEW 8.1 | Planned | Tenant isolation: `TenantId` and RLS on every tenant-owned table; test fails when one lacks them (ADR-0043). |
| NEW 8.2 | Planned | Pooled-connection leak test; `SET LOCAL` only (ADR-0043). |
| NEW 8.3 | Planned | Event envelope `TenantId` mandatory; dead-letter on missing tenant (ADR-0043). |
| NEW 8.4 | Planned | No just-in-time user creation; suspended user blocked immediately (ADR-0044). |
| NEW 8.5 | Planned | Mandatory platform rules cannot be overridden by a tenant (ADR-0046). |
| NEW 8.6 | Planned | Tenant is never a Prometheus label (ADR-0043). |
| NEW 8.7 | Known gap | Single platform admin and single approver; recovery for tenant admins has no second person (ADR-0044). |
| NEW 8.8 | Planned | Live ICEGATE filing disabled for rows only PO-verified (ADR-0049). |
