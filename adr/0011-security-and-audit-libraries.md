# ADR-0011: Security & Audit Platform Libraries

**Status:** Accepted
**Date:** 2026-08-12
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Day 17 of the Day-Wise Plan calls for two more shared Platform libraries: shared auth middleware and a generic audit-trail module (event type, old/new value, user, timestamp, correlation ID), per PRD audit requirements. `adr/0004` (Security & Audit Standards) specified the design for both — JWT/OAuth2 authentication, role-based policies mapped to the Day 1 Permissions Matrix, and a single shared `AuditEvent` model with a Business/Technical category split — but neither had been implemented until now.

No real Identity service exists yet (that's Phase 0, Day 23), so this ADR is explicit about what "shared auth middleware" means at this stage of the project.

## Decision

1. **`CustomsClearance.Platform.Security`** — `PlatformRoles` (Broker, Supervisor, ComplianceOfficer, Admin — exact names from the Day 1 Permissions Matrix), `PlatformPolicies` (RequireAdmin, RequireSupervisorOrAdmin, RequireComplianceOfficerOrAdmin, RequireAnyPlatformRole), `JwtOptions` (bound from an `"Jwt"` config section), and `AddCustomsClearanceAuthentication()`/`AddCustomsClearanceAuthorization()` extension methods. Authentication uses **symmetric-key JWT validation** for now — a deliberate, documented stand-in for the real Authority-based OIDC validation that Phase 0's Identity service (Day 23) will provide. `JwtOptions`' XML doc comment states explicitly that the intended migration is a configuration change (Authority + audience), not a rewrite of anything that consumes roles/policies/`[Authorize]`.
2. **`CustomsClearance.Platform.Audit`** — `AuditEvent` (the exact model from `adr/0004` Section 7: `AuditEventId`, `CorrelationId`, `EventCategory`, `EntityType`, `EntityId`, `Action`, `PerformedBy`/`PerformedBySystem`, `OccurredAt`, `BusinessRuleRef`, `FieldChanges`, `Classification`), `FieldChange`, `AuditEventCategory` (BusinessAudit/TechnicalAudit), `DataClassification` (the four-tier scheme from `adr/0004` Section 6), and an `IAuditWriter` port with two implementations: `ConsoleAuditWriter` (the functional default — structured log output, not a no-op) and `InMemoryAuditWriter` (test-only, captures events for assertions). `AuditEvent.Create()` auto-stamps `AuditEventId`, `OccurredAt` (UTC), and `CorrelationId` (from `Platform.Logging`'s `CorrelationIdAccessor`) so callers only supply what actually varies per event.

## Verification Against the Real Host, Not Just Isolated Tests

Both libraries were wired into `src/Api` (a new `Development`-only `/diagnostics/secure` endpoint, requiring `PlatformPolicies.RequireAdmin` and writing an audit event on success) and exercised with real, independently-minted JWTs — not just unit tests asserting policy objects were constructed correctly.

| Check | Result |
|---|---|
| `curl /diagnostics/secure`, no token | `401` |
| Same endpoint, JWT minted with `role=Admin`, signed with the exact dev key from `appsettings.Development.json` | `200`, response body includes `auditEventId` |
| Same endpoint, JWT minted with `role=Broker` (wrong role) | `403` |
| Server console log for the successful request | `AUDIT [TechnicalAudit] SecureEndpointAccessed on Diagnostics (null) by DiagnosticsEndpoint (CorrelationId=<id>, ...)` — the `CorrelationId` in the audit log line matched the `X-Correlation-Id` response header **exactly** |
| Unit tests (`Platform.Security.Tests`) | 3 tests, minting real JWTs via `System.IdentityModel.Tokens.Jwt` and sending them through an actual `TestServer` pipeline (not mocked) — 401/200/403 all independently reproduced |
| Unit tests (`Platform.Audit.Tests`) | 6 tests, 96.1% coverage |
| Integration tests (`Api.IntegrationTests`, `WebApplicationFactory`) | 6/6 passed total (3 new for `/diagnostics/secure`, mirroring the manual curl verification as repeatable automated tests) |
| `docker build` / `smoke-test.sh` | Passed on first attempt this time — `src/Api/Dockerfile` was updated to copy the two new Platform projects into the build context *before* running the smoke test, applying the Day 16 lesson proactively instead of discovering the same failure again |

## Findings Worth Recording

1. **Coverlet instruments the full transitive dependency graph, not just the assembly under test.** `Platform.Audit.Tests` initially reported `Platform.Logging` at 13% coverage (dragged in via `Platform.Audit`'s `ProjectReference`), nearly failing the 80% gate for a library this test project doesn't even own. Fixed by adding `<Include>[CustomsClearance.Platform.Audit]*</Include>` to scope coverage to the project's own assembly — and applied retroactively to all four existing unit test projects for consistency, even where it hadn't yet caused a visible failure. This is now the standard pattern: every future test project's coverage properties must include this `Include` filter.
2. **`Microsoft.AspNetCore.Authentication.JwtBearer` is not part of the ASP.NET Core shared framework**, unlike most middleware types (`IApplicationBuilder`, etc.) — it ships as its own NuGet package. Discovered via a real build failure, not assumed; `FrameworkReference` alone was insufficient.
3. **SonarAnalyzer correctly flags hardcoded JWT signing keys (S6781), including in test code.** Rather than blanket-suppressing, the suppression is scoped narrowly (the specific line) with an inline comment explaining it's a test-only key never read from configuration — consistent with `CONTRIBUTING.md`'s "no unexplained new warnings" rule.
4. **A missing-assertion Sonar warning (S2699) on a "doesn't throw" test** was fixed by asserting `task.IsCompletedSuccessfully` explicitly rather than relying on the implicit "no exception escaped" behavior xUnit already provides — makes the test's intent explicit rather than relying on convention.

## Consequences

- Every future bounded context's API layer should call `app.UseAuthentication()`/`app.UseAuthorization()` (after `UseCustomsClearanceCorrelationId()`/`UseCustomsClearanceErrorHandling()`, per `src/Api/Program.cs`'s established order) and use `PlatformRoles`/`PlatformPolicies` rather than hardcoding role strings.
- Every future bounded context that needs to write audit records should inject `IAuditWriter` and call `AuditEvent.Create()` — not build a bespoke audit mechanism, per the shared-infrastructure principle already established for the Rules Engine (`adr/0002`).
- **`ConsoleAuditWriter` is not the production audit sink.** A real append-only PostgreSQL-backed `IAuditWriter` implementation (per `adr/0004` Section 9) is Phase 0/3 work, registered by overriding the DI registration `AddCustomsClearanceAudit()` currently sets up — this is the intended seam, not a gap to be surprised by later.
- **The symmetric JWT key is a genuine, if temporary, security posture** — appropriate for Phase -1/local development, explicitly not appropriate once real users or real data are involved. This is tracked as a forward dependency on Phase 0 Day 23 (Identity service), not left as an implicit assumption.
- The coverage-scoping fix (`<Include>` filter) is now a required part of every future test project's setup — worth adding as an explicit item to Gate 2 (Backend Review, `docs/quality-gates/Gate2_Backend_Review.md`) in a future pass, since it wasn't part of that checklist when written on Day 15.

## Related

[[ADR-0004]] (Security & Audit Standards — the design this ADR implements). [[ADR-0007]] (coverage gate — Finding extended here with the transitive-instrumentation issue). [[ADR-0009]] (test automation layering — the TestServer-based real-pipeline testing pattern, now extended to auth). [[ADR-0010]] (Logging & Error Handling — `Platform.Audit` depends on `Platform.Logging`'s `CorrelationIdAccessor`; the Dockerfile-must-track-new-dependencies lesson from that ADR applied proactively here). Day 1 Permissions Matrix (`PlatformRoles`/`PlatformPolicies` source of truth).
