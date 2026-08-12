# Day 16 Execution Log — Reusable Library: Logging & Error Handling

Phase -1 (Architecture & Engineering Preparation), Day 16 of 20 — first day of Week 4. Step-by-step transcript — companion to `Day16_Logging_and_Error_Handling_Libraries.docx` and `adr/0010-logging-and-error-handling-libraries.md`.

---

## Step 1 — Scaffold Platform.Logging

**Commands:**
```bash
mkdir -p src/Platform/Logging src/Platform/ErrorHandling tests/Unit/Platform.Logging.Tests tests/Unit/Platform.ErrorHandling.Tests
dotnet new classlib -n CustomsClearance.Platform.Logging -o src/Platform/Logging --force
dotnet add src/Platform/Logging package Microsoft.AspNetCore.Http.Abstractions --version 2.2.0
dotnet add src/Platform/Logging package Microsoft.Extensions.Logging.Abstractions --version 8.0.1
```

**Output:** restored successfully. Removed the generated `Class1.cs`.

---

## Step 2 — Write CorrelationIdAccessor and CorrelationIdMiddleware

Wrote `CorrelationIdAccessor.cs` (AsyncLocal-based ambient accessor) and `CorrelationIdMiddleware.cs` (reads `X-Correlation-Id`, generates if absent, echoes on response, pushes into `ILogger.BeginScope`).

**First build attempt failed:**
```bash
dotnet build src/Platform/Logging/CustomsClearance.Platform.Logging.csproj
```
`IApplicationBuilder` not found — the `Microsoft.AspNetCore.Http.Abstractions` NuGet package doesn't include it.

**Fix:** replaced the NuGet package reference with `<FrameworkReference Include="Microsoft.AspNetCore.App" />` — the standard way for a class library to use ASP.NET Core middleware types without becoming a Web SDK project.

**Re-verification:**
```bash
dotnet build src/Platform/Logging/CustomsClearance.Platform.Logging.csproj
```
**Output:** `Build succeeded. 0 Warning(s). 0 Error(s).`

---

## Step 3 — Scaffold and build Platform.ErrorHandling

**Commands:**
```bash
dotnet new classlib -n CustomsClearance.Platform.ErrorHandling -o src/Platform/ErrorHandling --force
```
Added `<FrameworkReference Include="Microsoft.AspNetCore.App" />` directly this time (learned from Step 2) plus a `ProjectReference` to `Platform.Logging`.

Wrote `DomainValidationException.cs` (with `ValidationFailure` records carrying PRD Business Rule IDs), `ProblemDetailsResponse.cs` (the exact RFC 7807 envelope from API Standards v1.0 Section 6), `ErrorMapper.cs` (pure mapping function, deliberately separate from the middleware for unit-testability), `ExceptionHandlingMiddleware.cs`, and `ApplicationBuilderExtensions.cs`.

**Command:**
```bash
dotnet build src/Platform/ErrorHandling/CustomsClearance.Platform.ErrorHandling.csproj
```
**Output:** `Build succeeded. 0 Warning(s). 0 Error(s).`

---

## Step 4 — Add both to the solution

```bash
dotnet sln add src/Platform/Logging/CustomsClearance.Platform.Logging.csproj
dotnet sln add src/Platform/ErrorHandling/CustomsClearance.Platform.ErrorHandling.csproj
```

---

## Step 5 — Write and verify Platform.Logging.Tests

Scaffolded via `dotnet new xunit`, added `coverlet.msbuild` (not `.collector`, per the ADR-0007 finding), embedded coverage properties in the `.csproj` (80% threshold — Platform libraries classified as shared/CRUD-tier). Wrote `CorrelationIdAccessorTests.cs` (3 tests, including an async-flow test).

**First coverage run showed the accessor tested but the middleware untested** — the 80% threshold would likely fail once the middleware's ~25 lines were counted against only ~6 tested lines. Added `Microsoft.AspNetCore.TestHost` and wrote `CorrelationIdMiddlewareTests.cs`, running the real middleware through an in-process `HostBuilder`/`TestServer` pipeline (2 tests: header supplied vs. absent).

**First test run failed to compile:**
```
error CS1061: 'HttpResponse' does not contain a definition for 'WriteAsync'
```
Missing `using Microsoft.AspNetCore.Http;` — added it.

**Command:**
```bash
dotnet test tests/Unit/Platform.Logging.Tests/CustomsClearance.Platform.Logging.Tests.csproj
```
**Output:**
```
Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5
CustomsClearance.Platform.Logging: Line 100%, Branch 100%, Method 100%
```

---

## Step 6 — Write and verify Platform.ErrorHandling.Tests

Same scaffold pattern. Wrote `ErrorMapperTests.cs` (3 tests, including one specifically asserting a secret string in an exception message never appears in the mapped response), `DomainValidationExceptionTests.cs` (3 tests), and `ExceptionHandlingMiddlewareTests.cs` (3 tests via the same `TestServer` pattern, this time composing both `UseCustomsClearanceCorrelationId()` and `UseCustomsClearanceErrorHandling()` together to prove they work correctly in combination).

**Command:**
```bash
dotnet sln add tests/Unit/Platform.ErrorHandling.Tests/CustomsClearance.Platform.ErrorHandling.Tests.csproj
dotnet test tests/Unit/Platform.ErrorHandling.Tests/CustomsClearance.Platform.ErrorHandling.Tests.csproj
```
**Output:**
```
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9
CustomsClearance.Platform.ErrorHandling: Line 100%, Branch 87.5%, Method 100%
Total: Line 100%, Branch 91.66%
```

---

## Step 7 — Wire both libraries into the real Api host

Updated `src/Api/Program.cs`: added `app.UseCustomsClearanceCorrelationId()` then `app.UseCustomsClearanceErrorHandling()` (order matters — documented inline), and a `Development`-only `/diagnostics/throw-validation-error` endpoint to prove the pipeline end-to-end.

```bash
dotnet add src/Api/CustomsClearance.Api.csproj reference src/Platform/Logging/CustomsClearance.Platform.Logging.csproj
dotnet add src/Api/CustomsClearance.Api.csproj reference src/Platform/ErrorHandling/CustomsClearance.Platform.ErrorHandling.csproj
dotnet build src/Api/CustomsClearance.Api.csproj
```
**Output:** `Build succeeded. 0 Warning(s). 0 Error(s).`

---

## Step 8 — Run the real Api and hit it with curl (not just automated tests)

**Commands:**
```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls "http://localhost:5290" &
curl -s http://localhost:5290/health
curl -s -i http://localhost:5290/diagnostics/throw-validation-error
curl -s -i -H "X-Correlation-Id: my-test-id-999" http://localhost:5290/diagnostics/throw-validation-error
```

**Output (no correlation header supplied):**
```
HTTP/1.1 422 Unprocessable Entity
Content-Type: application/problem+json
X-Correlation-Id: 418bac03-b0f8-4223-986c-8ff026e0a0bc

{"type":"...validation-failed","title":"Business rule validation failed","status":422,
 "detail":"This is a demonstration validation failure.",
 "instance":"/diagnostics/throw-validation-error",
 "correlationId":"418bac03-b0f8-4223-986c-8ff026e0a0bc",
 "errors":[{"field":"demoField","code":"BR-DEMO-001","message":"..."}]}
```

**Output (correlation header supplied):** identical shape, `correlationId` and `X-Correlation-Id` header both echo `my-test-id-999` exactly, not a newly generated ID.

**Server log check:**
```bash
grep -i correlation /tmp/api_day16.log | tail -5
```
```
Business rule validation failed. CorrelationId=418bac03-b0f8-4223-986c-8ff026e0a0bc
Business rule validation failed. CorrelationId=my-test-id-999
```
Confirmed: the log scope carried the exact same correlation ID as the HTTP response, end to end.

Process killed afterward (`pkill -f "CustomsClearance.Api"`).

---

## Step 9 — Add an integration test capturing this behavior automatically

Wrote `tests/Integration/Api.IntegrationTests/ErrorHandlingEndpointTests.cs` (2 tests) — the same scenario just verified manually, now as a repeatable automated test via `WebApplicationFactory`.

**First build failed:**
```
error CS1061: 'IWebHostBuilder' does not contain a definition for 'UseEnvironment'
```
Missing `using Microsoft.AspNetCore.Hosting;` — added it.

**Command:**
```bash
bash build/scripts/integration-test.sh
```
**Output:** `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3` (health + 2 new tests).

---

## Step 10 — Full solution lint pass surfaces real naming violations

**Command:**
```bash
dotnet format --verify-no-changes --severity warn
```
**Output:**
```
warning IDE1006: Naming rule violation: Missing prefix: '_'  (ErrorMapper.cs, x2)
warning IDE1006: Naming rule violation: Missing prefix: '_'  (ExceptionHandlingMiddleware.cs)
```
Three private static fields (`ValidationFailedType`, `UnexpectedErrorType`, `SerializerOptions`) violated the Day 8 `_camelCase` private-field convention.

**Fix attempt via `sed` had a side effect:** the blanket `s/SerializerOptions/_serializerOptions/g` replacement also corrupted the *type name* `JsonSerializerOptions` (which contains the substring `SerializerOptions`) into `Json_serializerOptions`, breaking the build. Caught immediately by the next build step, not left unnoticed.

**Fix:** corrected the mangled type name back to `JsonSerializerOptions` via a targeted `Edit`, keeping the field rename.

**Re-verification:**
```bash
dotnet build
dotnet format --verify-no-changes --severity warn
```
**Output:** `0 Warning(s), 0 Error(s)`; format clean.

---

## Step 11 — A real Dockerfile bug found and fixed

**Command:**
```bash
rm -rf TestResults src/Api/{bin,obj} src/Platform/{Common,Logging,ErrorHandling}/{bin,obj} tests/Unit/*/{bin,obj,TestResults} tests/Integration/*/{bin,obj}
bash build/scripts/build.sh && bash build/scripts/lint.sh
```
This combined run hit the container's 2-minute foreground timeout (moved to background automatically) and the build step reported `1 Error(s)` — traced to the new `ErrorHandlingEndpointTests.cs` (Step 9's file) needing the `Microsoft.AspNetCore.Hosting` `using` that had already been added; re-checked and confirmed the build was actually clean once isolated (`dotnet build` alone: `0 Warning(s), 0 Error(s)`) — the timeout was a tooling artifact of running multiple heavy steps chained together, not a real regression.

**Real find:** `src/Api/Dockerfile` (from Day 13) only copied `src/Platform/Common/` into the build context. Since Api now references `Platform.Logging` and `Platform.ErrorHandling` too, `docker build` would fail at the `dotnet restore`/`publish` step inside the container.

**Fix:** added `COPY` steps for both new projects' `.csproj` files (pre-restore) and full source (post-restore) to `src/Api/Dockerfile`, mirroring the existing `Platform.Common` pattern.

**Verification:**
```bash
bash build/scripts/smoke-test.sh
```
**Output:**
```
Api healthy after 2 attempt(s):
{"status":"Healthy","service":"CustomsClearance.Api"}
== Tearing down dev environment ==
 ... Removed / Removed / Removed
```
Confirmed the container builds and runs correctly with both new library dependencies included.

---

## Step 12 — Full aggregate coverage check across all unit test projects

**Command:**
```bash
rm -rf TestResults tests/Unit/*/{bin,obj,TestResults} tests/Integration/*/{bin,obj}
bash build/scripts/test.sh
```
**Output:**
```
Assemblies: 3, Classes: 11, Files: 9
Line coverage: 100%  (78 of 78 coverable lines)
Method coverage: 100% (25 of 25)

CustomsClearance.Platform.Common          100%
CustomsClearance.Platform.ErrorHandling   100%  (7 classes, all 100%)
CustomsClearance.Platform.Logging         100%  (3 classes, all 100%)
```
All three unit test projects (existing `Platform.Common.Tests` plus the two new ones) pass together with genuine, non-vacuous 100% coverage.

---

## Step 13 — Record the decision

**File added:** `adr/0010-logging-and-error-handling-libraries.md` — covers both libraries' design, the full local verification table (Steps 8/11/12 above), the pure-function/middleware separation rationale, the no-internal-leakage security principle, and the Dockerfile bug as a documented consequence (kept as a forward warning for the next library addition, since there's no automated check yet that would catch it again).

---

## Summary of Files Changed/Added This Day

- `src/Platform/Logging/` — new (csproj, `CorrelationIdAccessor.cs`, `CorrelationIdMiddleware.cs`, `ApplicationBuilderExtensions.cs`)
- `src/Platform/ErrorHandling/` — new (csproj, `DomainValidationException.cs`, `ProblemDetailsResponse.cs`, `ErrorMapper.cs`, `ExceptionHandlingMiddleware.cs`, `ApplicationBuilderExtensions.cs`)
- `tests/Unit/Platform.Logging.Tests/` — new (3 test files, 5 tests)
- `tests/Unit/Platform.ErrorHandling.Tests/` — new (3 test files, 9 tests)
- `tests/Integration/Api.IntegrationTests/ErrorHandlingEndpointTests.cs` — new (2 tests)
- `src/Api/Program.cs` — updated (middleware wired in, diagnostics endpoint added)
- `src/Api/CustomsClearance.Api.csproj` — updated (2 new project references)
- `src/Api/Dockerfile` — updated (bug fix: copy the 2 new Platform projects into the build context)
- `CustomsClearance.sln` — updated (4 new projects added)
- `adr/0010-logging-and-error-handling-libraries.md` — new
- `Phase-1_Architecture_Engineering_Prep/Day16_Logging_and_Error_Handling_Libraries.docx` — new
- `Phase-1_Architecture_Engineering_Prep/Day16_Execution_Log.md` — this file

Not yet committed or pushed — pending review and commit-message request, per the established workflow.
