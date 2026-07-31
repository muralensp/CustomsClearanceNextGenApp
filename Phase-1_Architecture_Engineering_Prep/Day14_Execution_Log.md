# Day 14 Execution Log — Test Automation Scaffolding

Phase -1 (Architecture & Engineering Preparation), Day 14 of 20. Step-by-step transcript — companion to `Day14_Test_Automation_Scaffolding.docx` and `adr/0009-test-automation-scaffolding.md`.

---

## Step 1 — Check tooling availability

**Commands:**
```bash
node --version
npm --version
npx --version
```
**Output:**
```
v25.2.1
11.6.2
11.6.2
```
Confirmed Node/npm/npx available for the Newman-based contract test layer.

---

## Step 2 — Scaffold the integration test project

**Commands:**
```bash
dotnet new xunit -n CustomsClearance.Api.IntegrationTests -o tests/Integration/Api.IntegrationTests --force
dotnet add tests/Integration/Api.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing --version 8.0.8
dotnet add tests/Integration/Api.IntegrationTests reference ../../../src/Api/CustomsClearance.Api.csproj
```
**Output:** template created, package restored, project reference added successfully. Removed the generated `UnitTest1.cs` sample.

---

## Step 3 — Write the sample integration test

Added `HealthEndpointTests.cs`: a single `[Fact]` using `WebApplicationFactory<Program>` to spin up the real Api host in-process and assert `GET /health` returns 200 with the expected JSON body.

**Command:**
```bash
dotnet sln add tests/Integration/Api.IntegrationTests/CustomsClearance.Api.IntegrationTests.csproj
```

---

## Step 4 — Verify the integration test passes, standalone

**Command:**
```bash
dotnet test tests/Integration/Api.IntegrationTests/CustomsClearance.Api.IntegrationTests.csproj
```
**Output:**
```
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
```

---

## Step 5 — Confirm the whole solution still builds/lints clean with the new project

**Commands:**
```bash
dotnet build
dotnet format --verify-no-changes --severity warn
```
**Output:** `Build succeeded. 0 Warning(s). 0 Error(s).`; format clean (no output).

---

## Step 6 — Fix test.sh's scoping before it silently mixes unit + integration

**Problem identified:** the existing `test.sh` ran a bare `dotnet test` (solution-wide), which — now that an integration test project exists in the same `.sln` — would sweep integration tests into the unit+coverage job, diluting the coverage-threshold policy (a unit-test-specific policy per ADR-0007) with a project that doesn't declare coverage properties at all.

**Fix:** changed `test.sh` to loop over `tests/Unit/*/*.csproj` explicitly instead of calling bare `dotnet test`.

---

## Step 7 — Write integration-test.sh (new, separate script)

Loops over `tests/Integration/*/*.csproj`, mirroring the same portable-script pattern as `build.sh`/`lint.sh`/`test.sh`. No coverage threshold enforced — deliberate, per ADR-0009's reasoning.

---

## Step 8 — Verify both scripts work correctly and stay isolated

**Commands:**
```bash
rm -rf TestResults tests/Unit/Platform.Common.Tests/{TestResults,bin,obj} \
       tests/Integration/Api.IntegrationTests/{bin,obj} src/Api/{bin,obj} src/Platform/Common/{bin,obj}
bash build/scripts/build.sh
bash build/scripts/test.sh
bash build/scripts/integration-test.sh
```
**Output:**
```
=== UNIT TEST ===
CustomsClearance.Platform.Common                              100%
  CustomsClearance.Platform.Common.PlaceholderModuleMarker    100%

=== INTEGRATION TEST ===
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
```
Confirmed: unit job only reports Platform.Common coverage (integration project correctly excluded); integration job runs only the integration project.

---

## Step 9 — Write the Postman collection and environment

`postman/CustomsClearance.postman_collection.json`: one request, `GET {{baseUrl}}/health`, with three test assertions (status code 200, `Content-Type` header present, response body `status`/`service` fields match expected values).

`postman/CustomsClearance.postman_environment.json`: `baseUrl = http://localhost:8080`, matching the docker-compose port mapping from Day 13.

---

## Step 10 — Write contract-test.sh (new script)

Reuses the same build/wait-for-health pattern as Day 13's `smoke-test.sh` (`docker compose up -d --build`, poll `/health` up to 20 times), then runs `npx --yes newman run ...` with a JUnit reporter, then tears down via a `trap cleanup EXIT`.

---

## Step 11 — Run the actual script for real (first cold run, includes npx download)

**Command:**
```bash
mkdir -p TestResults
bash build/scripts/contract-test.sh
```

This took long enough (Newman's first-time `npx` package download) that it was run as a background task and polled for completion rather than blocking inline.

**Output (full):**
```
Api healthy after 1 attempt(s).

CustomsClearance API Contract Tests

-> Health Check
  GET http://localhost:8080/health [200 OK, 201B, 68ms]
  [ok] Status code is 200
  [ok] Response has correlationId-friendly JSON content type
  [ok] Body reports Healthy status

  iterations: 1/1   requests: 1/1   assertions: 3/3   failed: 0
  total run duration: 155ms

== Tearing down dev environment ==
 Container ... Removed
 Volume ... Removed
 Network ... Removed
```

**Verification of output artifacts:**
```bash
cat TestResults/newman-results.xml | head -5
docker ps -a | grep customsclerance
```
**Output:** valid JUnit XML present (`tests="1" ... failures="0"`); no leftover containers.

---

## Step 12 — Wire all three layers into CI (GitHub Actions)

**Change to `.github/workflows/ci.yml`:**
- Added `integration-test` job ("Integration Tests"), depending on `build`, calling `integration-test.sh`, uploading `TestResults/` as an artifact.
- Added `contract-test` job ("API Contract Tests (Postman/Newman)"), depending on `build`, calling `contract-test.sh`, dumping `docker compose logs` on failure, uploading `TestResults/newman-results.xml`.
- Updated the workflow's header comment to describe the Day 14 additions.

---

## Step 13 — Wire into CI (Azure Pipelines, kept in lockstep per ADR-0005)

**Change to `build/azure-pipelines.yml`:**
- Added `IntegrationTest` stage, calling `integration-test.sh`, publishing VSTest-format results.
- Added `ContractTest` stage, calling `contract-test.sh`, dumping logs on failure, publishing JUnit-format results from `newman-results.xml`.
- Updated header comment.

---

## Step 14 — Record the decision

**File added:** `adr/0009-test-automation-scaffolding.md` — covers all three layers, why they're kept in separate scripts/jobs, the local verification table (Steps 4/8/11 above), and forward-looking consequences (the Postman collection must grow alongside real OpenAPI-defined endpoints; `npx newman`'s cold-download time may need addressing later).

---

## Step 15 — Final full local re-verification (all four scripts, clean state)

**Commands:**
```bash
rm -rf TestResults src/Api/{bin,obj} src/Platform/Common/{bin,obj} \
       tests/Unit/Platform.Common.Tests/{bin,obj,TestResults} \
       tests/Integration/Api.IntegrationTests/{bin,obj}
bash build/scripts/build.sh
bash build/scripts/lint.sh
bash build/scripts/test.sh
bash build/scripts/integration-test.sh
```
**Output:** all four green — 0 warnings/errors, clean lint, 100% unit coverage (threshold satisfied), 1/1 integration test passed. (`smoke-test.sh` and `contract-test.sh` re-confirmed working in Step 11 above; not re-run a second time in this final pass to avoid redundant docker rebuild time.)

---

## Summary of Files Changed/Added This Day

- `tests/Integration/Api.IntegrationTests/` — new (csproj, `HealthEndpointTests.cs`)
- `postman/CustomsClearance.postman_collection.json`, `postman/CustomsClearance.postman_environment.json` — new
- `build/scripts/integration-test.sh`, `build/scripts/contract-test.sh` — new
- `build/scripts/test.sh` — updated (scoped to `tests/Unit/*/*.csproj`)
- `.github/workflows/ci.yml` — updated (two new jobs)
- `build/azure-pipelines.yml` — updated (two new stages, kept in lockstep)
- `CustomsClearance.sln` — updated (integration test project added)
- `adr/0009-test-automation-scaffolding.md` — new
- `Phase-1_Architecture_Engineering_Prep/Day14_Test_Automation_Scaffolding.docx` — new
- `Phase-1_Architecture_Engineering_Prep/Day14_Execution_Log.md` — this file

Not yet committed or pushed — pending review and commit-message request, per the established workflow.
