# ADR-0010: Logging & Error Handling Platform Libraries

**Status:** Accepted
**Date:** 2026-08-06
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Day 16 of the Day-Wise Plan calls for the first two shared Platform libraries: a logging module (structured logs, correlation ID propagation) and standard exception/error-response handling. These were referenced but not implemented in earlier days — Day 6's API Standards specified the correlation ID and RFC 7807 error format, Day 8's Coding Standards specified the `DomainValidationException`/`BusinessRuleCode` pattern, and Gate 2 of the Day 15 quality gates checklist assumes both already exist. Day 16 builds them for real.

## Decision

Two separate class libraries under `src/Platform/`, per ADR-0003's structure:

1. **`CustomsClearance.Platform.Logging`** — `CorrelationIdAccessor` (an `AsyncLocal<string?>` ambient accessor), `CorrelationIdMiddleware` (reads `X-Correlation-Id` from the request, generates one if absent, echoes it on the response, pushes it into the structured logging scope via `ILogger.BeginScope`), and a `UseCustomsClearanceCorrelationId()` extension method.
2. **`CustomsClearance.Platform.ErrorHandling`** — `DomainValidationException` (carries one or more `ValidationFailure` records, each with a PRD Business Rule ID `Code`), `ProblemDetailsResponse`/`ProblemDetailsError` (the exact RFC 7807-shaped envelope from API Standards v1.0 Section 6), a pure `ErrorMapper.Map()` function (Exception → `ProblemDetailsResponse`, deliberately separated from the middleware for unit-testability per `adr/0009`), `ExceptionHandlingMiddleware`, and a `UseCustomsClearanceErrorHandling()` extension method. References `Platform.Logging` (both are Platform shared libraries, not separate bounded contexts, so this internal dependency is not subject to the cross-context restriction from Day 5).

Both are plain class libraries using `<FrameworkReference Include="Microsoft.AspNetCore.App" />` rather than the Web SDK — the standard way for a shared library to use ASP.NET Core middleware types without becoming its own web application.

## Verification (Not Just Unit Tests in Isolation)

Both libraries were wired into the actual `src/Api` placeholder host (not just tested standalone) and exercised for real:

| Check | Result |
|---|---|
| `curl /diagnostics/throw-validation-error` (no correlation header supplied) | `422`, `application/problem+json`, a generated GUID correlation ID present in both the response body and the `X-Correlation-Id` response header |
| Same request, with `X-Correlation-Id: my-test-id-999` supplied | The exact supplied ID echoed back, not a newly generated one |
| Server console log during the above requests | `Business rule validation failed. CorrelationId=<matching id>` — confirming the log scope correctly carried the same correlation ID as the HTTP response |
| Unit tests (`Platform.Logging.Tests`, `Platform.ErrorHandling.Tests`) | 5 + 9 = 14 tests, 100% line coverage on both libraries, including the middleware itself (via `Microsoft.AspNetCore.TestHost`, not just the pure logic) |
| Integration tests (`Api.IntegrationTests`, `WebApplicationFactory`) | 3/3 passed, including two new tests exercising the same error-handling behavior through the full ASP.NET Core pipeline |
| `docker build` / `smoke-test.sh` | Initially failed to account for the two new project dependencies in `src/Api/Dockerfile` (only `Platform.Common` was copied into the build context) — caught and fixed before being considered done, not left as a known gap |

## Design Notes

- **`ErrorMapper.Map()` is a pure function**, not a method on the middleware, specifically so its logic (which exception maps to which status code, and — critically — that unknown exceptions never leak internal details like connection strings into the response body) can be unit-tested without any HTTP infrastructure, consistent with `adr/0009`'s "unit tests should not require infrastructure" principle.
- **Unknown/unhandled exceptions deliberately return no `Detail` and no `Errors`** in the response — only `DomainValidationException` (an expected, business-rule-driven failure) gets its message and failures surfaced to the caller. This directly implements the Day 10 Security Standards principle (Section 11) that internal details must never leak externally, applied here to the error response body, not just log output.
- **Ordering matters and is documented in code, not just in this ADR**: `UseCustomsClearanceCorrelationId()` must run before `UseCustomsClearanceErrorHandling()`, or a mapped error response would have no correlation ID to include. Both extension methods' XML doc comments state this explicitly, and `src/Api/Program.cs` demonstrates the correct order.
- **A `/diagnostics/throw-validation-error` endpoint was added to the Api host, gated to `Development` only.** It exists purely to prove the pipeline works against a real host (mirroring the `/health` endpoint's role from Day 13) — it is not a real business endpoint and should not be mistaken for one by a future reader.

## Consequences

- Every future bounded context's API layer should call `app.UseCustomsClearanceCorrelationId()` and `app.UseCustomsClearanceErrorHandling()` (in that order) as part of its own host setup, and throw `DomainValidationException` (not a bespoke exception type) for business rule violations, per Day 8's original intent now made concrete.
- `docker-compose.yml`/`src/Api/Dockerfile` must be kept in sync with the Api project's actual `ProjectReference` list going forward — this was caught once here; there is no automated check yet that would catch it again for a future library addition (a reasonable future improvement, not implemented today, given the project's current scale).
- The `/diagnostics/throw-validation-error` endpoint should be removed or replaced once a real context endpoint naturally exercises `DomainValidationException` in Phase 3+, so it doesn't linger as permanent placeholder code.

## Related

[[ADR-0003]] (bounded-context-first structure — Platform libraries live outside that restriction). [[ADR-0006]] (Clean Architecture adaptation — these are Platform-tier shared libraries, not Domain/Application/Infrastructure for any specific context). [[ADR-0007]] (coverage gate — both libraries classified as CRUD/shared-library tier, 80%). [[ADR-0009]] (test automation layering — the pure-function/middleware split and the TestServer-based unit test pattern both follow its principles). Day 6 API Standards v1.0 (Sections 6-7, the exact contract these libraries implement). Day 8 Coding Standards v1.0 (Section 4, the `DomainValidationException`/`BusinessRuleCode` pattern this ADR concretizes). Day 10 Security & Audit Standards (Section 11, the no-internal-leakage principle).
