# Day 13 Execution Log — CD Pipeline & Environments

Phase -1 (Architecture & Engineering Preparation), Day 13 of 20. Step-by-step transcript of what was actually run and what each step produced — companion to `Day13_CD_Pipeline_and_Environments.docx` (narrative deliverable) and `adr/0008-cd-pipeline-and-environment-promotion.md` (decision record).

---

## Step 0 — Scope clarification before doing anything

Asked the user how far "dev environment" should go, since no cloud provider/hosting target has been chosen yet (that's Phase 0, Day 21). Three options presented: (1) containerize + publish to GHCR only, (2) containerize + docker-compose smoke test, (3) containerize + deploy to a real hosted environment now.

**User's choice:** Option 2 — containerize + docker-compose smoke test. No external account, no persistent hosting, proves the image runs correctly via a health check.

---

## Step 1 — Check Docker availability

**Command:**
```bash
docker --version
docker compose version
```

**Output:**
```
Docker version 29.4.1, build 055a478
Docker Compose version v5.1.3
```

---

## Step 2 — Scaffold a minimal Api host

No `src/Api` project existed yet (empty folder from Day 9). Needed something real to containerize.

**Command:**
```bash
dotnet new webapi --use-minimal-apis -n CustomsClearance.Api -o . --force
```

**Output:** template created successfully, restored cleanly. Generated the standard WeatherForecast sample plus `Program.cs`, `appsettings*.json`, `Properties/`, and a `.http` file.

**Cleanup:** removed the sample `.http` file (`rm -f src/Api/CustomsClearance.Api.http`).

---

## Step 3 — Strip the template down to a placeholder + health endpoint

Replaced the generated `Program.cs` (WeatherForecast sample) with a minimal version exposing only `/health`, returning `{"status":"Healthy","service":"CustomsClearance.Api"}`. Removed `UseHttpsRedirection()` deliberately, since the container serves plain HTTP for the smoke test (no cert configured).

Added the project to the solution:
```bash
dotnet sln add src/Api/CustomsClearance.Api.csproj
```

**Output:** `Project 'src\Api\CustomsClearance.Api.csproj' added to the solution.`

---

## Step 4 — First build surfaces two real SonarAnalyzer warnings

**Command:**
```bash
dotnet build src/Api/CustomsClearance.Api.csproj
```

**Output:**
```
warning S1118: Add a 'protected' constructor or the 'static' keyword to the class declaration.
warning S6966: Await RunAsync instead.
    2 Warning(s)
```

Both from the `public partial class Program { }` pattern (needed later for `WebApplicationFactory`-based integration tests) and `app.Run()`.

**Fix applied:**
- `app.Run();` → `await app.RunAsync();`
- `public partial class Program { }` → added a `protected Program() { }` constructor

**Re-verification:**
```bash
dotnet build src/Api/CustomsClearance.Api.csproj
dotnet format --verify-no-changes --severity warn
```
**Output:** `0 Warning(s)`, `0 Error(s)`; lint clean.

---

## Step 5 — Confirm the health endpoint works before containerizing

**Command:**
```bash
cd src/Api
(dotnet run --urls "http://localhost:5289" > /tmp/api_run.log 2>&1 &)
sleep 8
curl -s http://localhost:5289/health
```

**Output:**
```
{"status":"Healthy","service":"CustomsClearance.Api"}
```

Process killed afterward (`pkill -f "CustomsClearance.Api"`).

---

## Step 6 — Write the Dockerfile and .dockerignore

Multi-stage `src/Api/Dockerfile`: `mcr.microsoft.com/dotnet/sdk:8.0` build stage → `mcr.microsoft.com/dotnet/aspnet:8.0` runtime stage. Build context set to the repo root (not `src/Api/`) since the Api project references sibling projects (`Platform.Common`) per ADR-0003's bounded-context-first structure.

`.dockerignore` added at repo root excluding `bin/`, `obj/`, `TestResults/`, docs, ADRs, and markdown files from the build context.

---

## Step 7 — Build the image directly (no compose yet)

**Command:**
```bash
docker build -f src/Api/Dockerfile -t customsclearance-api:dev .
```

**Output:** multi-stage build completed successfully — restore (~10s), publish (~19s), image exported and tagged `customsclearance-api:dev`.

---

## Step 8 — Run the built image directly and verify health

**Commands:**
```bash
docker run -d --name customsclearance-api-test -p 8080:8080 customsclearance-api:dev
sleep 5
curl -sf http://localhost:8080/health
docker logs customsclearance-api-test
```

**Output:**
```
{"status":"Healthy","service":"CustomsClearance.Api"}
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://[::]:8080
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

**Cleanup:**
```bash
docker stop customsclearance-api-test
docker rm customsclearance-api-test
```

---

## Step 9 — Write docker-compose.yml (the "dev environment" definition)

Added `docker-compose.yml` at repo root: an `api` service (built from `src/Api/Dockerfile`) and a `postgres` service (`postgres:16-alpine`, per ADR-0001's stack). Explicitly commented that the Api does not yet connect to Postgres — its presence reflects the stack decision, not existing database integration.

---

## Step 10 — Verify the full compose stack starts and is reachable

**Command:**
```bash
docker compose up -d --build
sleep 6
docker compose ps
curl -sf http://localhost:8080/health
docker compose logs postgres
```

**Output:**
```
NAME                                   STATUS          PORTS
customsclerancenextgenapp-api-1        Up 24 seconds   0.0.0.0:8080->8080/tcp
customsclerancenextgenapp-postgres-1   Up 25 seconds   0.0.0.0:5432->5432/tcp

{"status":"Healthy","service":"CustomsClearance.Api"}

postgres-1  | database system is ready to accept connections
```

**Cleanup:**
```bash
docker compose down -v
```

---

## Step 11 — Write build/scripts/smoke-test.sh (the actual CI-invoked script)

Portable script matching the pattern of `build.sh`/`lint.sh`/`test.sh` (ADR-0005): `docker compose up -d --build`, poll `/health` up to 20 times (3s apart), print success and exit 0, or dump logs and exit 1 on exhaustion. A `trap cleanup EXIT` ensures `docker compose down -v` always runs, success or failure.

---

## Step 12 — Run the actual script (not manual steps) to prove it matches what CI will do

**Command:**
```bash
bash build/scripts/smoke-test.sh
```

**Output:**
```
== Waiting for Api health check ==
Attempt 1/20: not ready yet, retrying in 3s...
Api healthy after 2 attempt(s):
{"status":"Healthy","service":"CustomsClearance.Api"}
== Tearing down dev environment ==
...
SCRIPT EXIT: 0
```

---

## Step 13 — Deliberately break it to prove the failure path works too

**Action:** temporarily pointed the health check at a closed port (`sed -i 's#localhost:8080/health#localhost:9999/health#'`).

**Command:**
```bash
timeout 90 bash build/scripts/smoke-test.sh
```

**Output:**
```
Attempt 3/20: not ready yet, retrying in 3s...
...
Attempt 16/20: not ready yet, retrying in 3s...
== Tearing down dev environment ==
EXIT: 124
```

Confirmed: retry loop behaves correctly, and the `trap cleanup EXIT` fires even when forcibly killed by `timeout` mid-retry — teardown is not skipped on failure.

**Revert:**
```bash
sed -i 's#localhost:9999/health#localhost:8080/health#' build/scripts/smoke-test.sh
docker compose down -v
```

---

## Step 14 — Wire into CI (GitHub Actions)

**Change to `.github/workflows/ci.yml`:** added a new `containerize` job ("Containerize + Dev Environment Smoke Test"), depending on `build`, running `bash build/scripts/smoke-test.sh`, with a `docker compose logs` step that only runs `if: failure()`. Updated the workflow's header comment to describe the Day 13 addition.

---

## Step 15 — Wire into CI (Azure Pipelines, kept in lockstep per ADR-0005)

**Change to `build/azure-pipelines.yml`:** added a matching `Containerize` stage (`ContainerizeAndSmokeTest` job) calling the same script, with a `docker compose logs` step gated on `condition: failed()`. Header comment updated.

---

## Step 16 — Record the decision

**File added:** `adr/0008-cd-pipeline-and-environment-promotion.md` — covers the containerization decision, the local verification table (Steps 7-13 above), the explicit scope boundary (no cloud provider chosen, nothing persistently hosted, no image published to a registry), and the dev → staging → prod environment promotion path policy (triggers and gates for each environment, mapped onto the Phase-wise Plan v2's existing Quality Gates table).

**Work item logged:** `PLAT-002` in `docs/WORK-ITEMS.md` — "Choose hosting target and container registry for Staging/Production," explicitly deferred to Phase 0, Day 21.

---

## Step 17 — Full solution re-verification (all four stages, clean state)

**Commands:**
```bash
rm -rf src/Api/{bin,obj} tests/Unit/Platform.Common.Tests/{TestResults,bin,obj} \
       TestResults src/Platform/Common/{bin,obj}
bash build/scripts/build.sh
bash build/scripts/lint.sh
bash build/scripts/test.sh
bash build/scripts/smoke-test.sh
```

**Output:**
```
=== BUILD ===     0 Warning(s), 0 Error(s)
=== LINT ===      (clean, no violations)
=== TEST ===      100% coverage (genuine), CustomsClearance.Platform.Common.PlaceholderModuleMarker
=== SMOKE TEST === containers removed, network removed, volume removed (clean teardown)
```

All four stages pass end-to-end with the new Api project present, confirming nothing regressed.

---

## Summary of Files Changed/Added This Day

- `src/Api/CustomsClearance.Api.csproj`, `Program.cs`, `Dockerfile`, `appsettings*.json`, `Properties/` — new
- `.dockerignore` — new
- `docker-compose.yml` — new
- `build/scripts/smoke-test.sh` — new
- `.github/workflows/ci.yml` — updated (new `containerize` job)
- `build/azure-pipelines.yml` — updated (new `Containerize` stage, kept in lockstep)
- `CustomsClearance.sln` — updated (Api project added)
- `adr/0008-cd-pipeline-and-environment-promotion.md` — new
- `docs/WORK-ITEMS.md` — updated (added `PLAT-002`)
- `Phase-1_Architecture_Engineering_Prep/Day13_CD_Pipeline_and_Environments.docx` — new
- `Phase-1_Architecture_Engineering_Prep/Day13_Execution_Log.md` — this file

Not yet committed or pushed — pending review and commit-message request, per the established workflow.
