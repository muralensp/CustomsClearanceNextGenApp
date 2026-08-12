# Day 17 Execution Log — Reusable Library: Security & Auditing

Phase -1 (Architecture & Engineering Preparation), Day 17 of 20. Step-by-step transcript — companion to `Day17_Security_and_Audit_Libraries.docx` and `adr/0011-security-and-audit-libraries.md`.

---

## Step 1 — Scaffold Platform.Security

```bash
dotnet new classlib -n CustomsClearance.Platform.Security -o src/Platform/Security --force
```
Added `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. Wrote `PlatformRoles.cs` (Broker/Supervisor/ComplianceOfficer/Admin, matching the Day 1 Permissions Matrix exactly) and `PlatformPolicies.cs` (RequireAdmin, RequireSupervisorOrAdmin, RequireComplianceOfficerOrAdmin, RequireAnyPlatformRole).

## Step 2 — Write JwtOptions and ServiceCollectionExtensions; first build fails

Wrote `JwtOptions.cs` and `ServiceCollectionExtensions.cs` (`AddCustomsClearanceAuthentication`, `AddCustomsClearanceAuthorization`).

```bash
dotnet build src/Platform/Security/CustomsClearance.Platform.Security.csproj
```
```
error CS0234: The type or namespace name 'JwtBearer' does not exist in the namespace 'Microsoft.AspNetCore.Authentication'
error CS0234: The type or namespace name 'IdentityModel' does not exist in the namespace 'Microsoft'
```
`Microsoft.AspNetCore.Authentication.JwtBearer` is not part of the shared framework, unlike most ASP.NET Core middleware — it's its own NuGet package. Added it:
```bash
dotnet add src/Platform/Security package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.8
```
Rebuild: `Build succeeded. 0 Warning(s). 0 Error(s).` Corrected the csproj's inaccurate comment (had claimed JwtBearer ships in the shared framework) to record what was actually learned.

## Step 3 — Scaffold Platform.Audit

```bash
dotnet new classlib -n CustomsClearance.Platform.Audit -o src/Platform/Audit --force
```
Added `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, and a `ProjectReference` to `Platform.Logging` (for `CorrelationIdAccessor`). Wrote `AuditEventCategory.cs`, `DataClassification.cs`, `FieldChange.cs`, `AuditEvent.cs` (with the `Create()` factory auto-stamping ID/timestamp/correlation ID and requiring either `PerformedBy` or `PerformedBySystem`), `IAuditWriter.cs`, `ConsoleAuditWriter.cs`, `InMemoryAuditWriter.cs`, `ServiceCollectionExtensions.cs`.

```bash
dotnet build src/Platform/Audit/CustomsClearance.Platform.Audit.csproj
```
`Build succeeded. 0 Warning(s). 0 Error(s).`

```bash
dotnet sln add src/Platform/Security/... src/Platform/Audit/...
```

## Step 4 — Write and run Platform.Audit.Tests; a real coverage-scoping bug found

Scaffolded, added `coverlet.msbuild` (not `.collector`), wrote `AuditEventTests.cs` (4 tests) and `WriterTests.cs` (2 tests, using `NullLogger`).

```bash
dotnet test tests/Unit/Platform.Audit.Tests/CustomsClearance.Platform.Audit.Tests.csproj
```
```
CustomsClearance.Platform.Audit    93.54%
CustomsClearance.Platform.Logging  13.63%   <- unexpected
Total                               72.61%  <- BELOW 80% THRESHOLD, FAILED
error: The total line coverage is below the specified 80
```
**Root cause:** coverlet instruments every assembly reachable via `ProjectReference`, not just the assembly under test — `Platform.Audit`'s reference to `Platform.Logging` pulled Logging's (mostly untested-by-this-project) code into the coverage calculation.

**Fix:** added `<Include>[CustomsClearance.Platform.Audit]*</Include>` to scope coverage to the project's own assembly.
```bash
dotnet test tests/Unit/Platform.Audit.Tests/CustomsClearance.Platform.Audit.Tests.csproj
```
```
CustomsClearance.Platform.Audit   93.54%
Total                              93.54%   PASS
```

**Applied retroactively** to `Platform.Logging.Tests.csproj`, `Platform.ErrorHandling.Tests.csproj`, and `Platform.Common.Tests.csproj` for consistency, even though none of them had yet failed because of this. Re-ran the full `test.sh` afterward — all four (plus the new Audit project) still passed correctly scoped.

## Step 5 — Write and run Platform.Security.Tests; real JWTs, real TestServer

Scaffolded, added `coverlet.msbuild`, `Microsoft.AspNetCore.TestHost`, `System.IdentityModel.Tokens.Jwt`. Wrote `AuthenticationAuthorizationTests.cs`: a `MintToken(role)` helper that signs a real JWT with a test key, and `InvokeSecuredEndpointAsync` that spins up a `TestServer` running the actual `AddCustomsClearanceAuthentication`/`AddCustomsClearanceAuthorization` pipeline behind a `RequireAuthorization(PlatformPolicies.RequireAdmin)` endpoint.

**First run failed twice**, each a real missing-wireup bug, not a flaky test:
```
InvalidOperationException: Unable to find the required services... 'IServiceCollection.AddRouting'
```
→ added `services.AddRouting()`.
```
InvalidOperationException: EndpointRoutingMiddleware matches endpoints setup by EndpointMiddleware and so must be added... Please add EndpointRoutingMiddleware by calling 'IApplicationBuilder.UseRouting'
```
→ added `app.UseRouting()` before `UseAuthentication`/`UseAuthorization`/`UseEndpoints`.

```bash
dotnet test tests/Unit/Platform.Security.Tests/CustomsClearance.Platform.Security.Tests.csproj
```
```
Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3
CustomsClearance.Platform.Security: 89.13%
```
All three scenarios (no token → 401, Admin token → 200, wrong-role token → 403) passed against a real pipeline with real signed tokens.

## Step 6 — Lint surfaces two more real findings

```bash
dotnet format --verify-no-changes --severity warn
```
```
warning S6781: JWT secret keys should not be disclosed  (x2, on the test key's declaration and usage)
warning IDE1006: Naming rule violation: Missing prefix: '_'  (x3, TestKey/TestIssuer/TestAudience)
warning S2699: Add at least one assertion to this test case  (WriterTests.ConsoleAuditWriter_CompletesWithoutThrowing)
```

**Fixes:**
- Renamed `TestKey`/`TestIssuer`/`TestAudience` → `_testKey`/`_testIssuer`/`_testAudience` (consistent with the Day 16 precedent for private const fields).
- Added a narrowly-scoped `#pragma warning disable S6781` / `restore` around the one line that actually constructs the `SymmetricSecurityKey`, with an inline comment explaining it's a test-only key never read from configuration.
- Renamed `ConsoleAuditWriter_CompletesWithoutThrowing` to `ConsoleAuditWriter_CompletesSuccessfully` and added an explicit `Assert.True(task.IsCompletedSuccessfully)`.

```bash
dotnet build && dotnet format --verify-no-changes --severity warn
```
`0 Warning(s), 0 Error(s)`; format clean.

## Step 7 — Wire both libraries into the real Api host

Updated `src/Api/Program.cs`: `AddCustomsClearanceAuthentication`/`AddCustomsClearanceAuthorization`/`AddCustomsClearanceAudit` registered, `app.UseAuthentication()`/`app.UseAuthorization()` added to the pipeline, and a new `Development`-only `/diagnostics/secure` endpoint (requires `RequireAdmin`, writes an audit event via injected `IAuditWriter`, returns the audit event's ID).

```bash
dotnet add src/Api reference src/Platform/Security/... src/Platform/Audit/...
```
Added a `Jwt` section to `appsettings.Development.json` with a clearly-labeled placeholder key (`LOCAL-DEV-ONLY-PLACEHOLDER-KEY-NEVER-USE-IN-PRODUCTION-32B`) — deliberately absent from the base `appsettings.json`, so production must supply real configuration via a secrets store.

```bash
dotnet build src/Api/CustomsClearance.Api.csproj
```
`Build succeeded. 0 Warning(s). 0 Error(s).`

## Step 8 — Run the real Api and mint real tokens with a standalone script

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls "http://localhost:5291" &
```
First `curl` attempt failed to connect (timing — the host was still starting); retried successfully once the log showed "Application started."

```bash
curl -s http://localhost:5291/health
# {"status":"Healthy","service":"CustomsClearance.Api"}

curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5291/diagnostics/secure
# 401
```

Wrote a standalone Python script (`mint_token.py`) to hand-construct and HMAC-SHA256-sign a JWT matching the exact dev key/issuer/audience, independent of any test framework — deliberately not reusing the C# test code, to prove the wire format is genuinely interoperable, not just internally consistent.

```bash
python3 mint_token.py > admin_token.txt
curl -s -i -H "Authorization: Bearer $(cat admin_token.txt)" http://localhost:5291/diagnostics/secure
```
```
HTTP/1.1 200 OK
X-Correlation-Id: be9faa6f-a8fd-40f8-a3a6-8229c430e886
{"message":"You are authenticated as Admin.","auditEventId":"88223fe8-..."}
```

```bash
grep -i "AUDIT\|correlation" /tmp/api_day17.log | tail -5
```
```
AUDIT [TechnicalAudit] SecureEndpointAccessed on Diagnostics (null) by DiagnosticsEndpoint (CorrelationId=be9faa6f-a8fd-40f8-a3a6-8229c430e886, ...)
```
Confirmed: the audit log's `CorrelationId` matched the HTTP response header exactly.

Minted a second token with `role=Broker` and confirmed `403`. Killed the running Api and cleaned up the temp Python scripts/tokens.

## Step 9 — Update the Dockerfile proactively (applying the Day 16 lesson before it could repeat)

Added `COPY` steps for `src/Platform/Security/` and `src/Platform/Audit/` to `src/Api/Dockerfile`, matching the existing pattern for `Common`/`Logging`/`ErrorHandling` — done *before* running the smoke test this time, rather than discovering the same class of failure again.

## Step 10 — Add integration tests for the new endpoint

Wrote `tests/Integration/Api.IntegrationTests/SecureEndpointTests.cs` (3 tests: no-token/401, Admin-token/200-with-auditEventId, wrong-role/403), reusing the same JWT-minting pattern as `Platform.Security.Tests` but against `appsettings.Development.json`'s actual configured key via `WebApplicationFactory`.

```bash
dotnet add tests/Integration/Api.IntegrationTests package System.IdentityModel.Tokens.Jwt --version 8.0.2
```

## Step 11 — Full pipeline re-verification

```bash
bash build/scripts/build.sh   # 0 Warning(s), 0 Error(s)
bash build/scripts/lint.sh    # clean
bash build/scripts/test.sh    # 5 unit test projects, all correctly scoped, all above threshold (96.4% aggregate)
bash build/scripts/integration-test.sh   # 6/6 passed
bash build/scripts/smoke-test.sh         # container built and became healthy on first attempt
```

## Step 12 — Record the decision

**File added:** `adr/0011-security-and-audit-libraries.md` — covers both libraries' design, the full local/real-host verification table, the two real findings (coverlet's transitive instrumentation, `JwtBearer` not being part of the shared framework), and forward consequences (the symmetric key is a genuine but temporary posture pending Phase 0 Day 23; `ConsoleAuditWriter` is not the production sink).

---

## Summary of Files Changed/Added This Day

- `src/Platform/Security/` — new (csproj, `PlatformRoles.cs`, `PlatformPolicies.cs`, `JwtOptions.cs`, `ServiceCollectionExtensions.cs`)
- `src/Platform/Audit/` — new (csproj, `AuditEventCategory.cs`, `DataClassification.cs`, `FieldChange.cs`, `AuditEvent.cs`, `IAuditWriter.cs`, `ConsoleAuditWriter.cs`, `InMemoryAuditWriter.cs`, `ServiceCollectionExtensions.cs`)
- `tests/Unit/Platform.Security.Tests/` — new (3 tests)
- `tests/Unit/Platform.Audit.Tests/` — new (6 tests)
- `tests/Integration/Api.IntegrationTests/SecureEndpointTests.cs` — new (3 tests)
- `src/Api/Program.cs` — updated (auth/audit wired in, `/diagnostics/secure` added)
- `src/Api/appsettings.Development.json` — updated (Jwt placeholder config)
- `src/Api/Dockerfile` — updated (2 new Platform projects copied into build context, proactively)
- `tests/Unit/Platform.{Logging,ErrorHandling,Common}.Tests/*.csproj` — updated (retroactive coverage `<Include>` scoping fix)
- `CustomsClearance.sln` — updated (5 new projects added)
- `adr/0011-security-and-audit-libraries.md` — new
- `Phase-1_Architecture_Engineering_Prep/Day17_Security_and_Audit_Libraries.docx` — new
- `Phase-1_Architecture_Engineering_Prep/Day17_Execution_Log.md` — this file

Not yet committed or pushed — pending review and commit-message request, per the established workflow.
