# Day 18 Execution Log — Reusable Library: Persistence & Messaging

Phase -1 (Architecture & Engineering Preparation), Day 18 of 20. This was the most bug-dense day of the project so far — five real, independent bugs found and fixed, each via actually running the system rather than trusting a partial check. Full technical detail of each finding is in `adr/0012-persistence-and-messaging-libraries.md`; this log is the condensed command-by-command version.

---

## Steps 1-4 — Scaffold and build both libraries

- `dotnet new classlib` for `Platform.Persistence` and `Platform.Messaging`.
- Persistence: added `Microsoft.EntityFrameworkCore` + `Npgsql.EntityFrameworkCore.PostgreSQL` — first restore failed (`NU1605: package downgrade`, EF Core 8.0.8 vs Npgsql's requirement of 8.0.10+); fixed by aligning EF Core to 8.0.10.
- Wrote `AuditableEntity<TId>`, `IRepository<TEntity,TId>`, `PlatformDbContext` (auto-stamps `CreatedAt`/`UpdatedAt` in `SaveChangesAsync`), `EfRepository<TEntity,TId>` (deliberately does not call `SaveChangesAsync` itself — Unit of Work stays with the caller).
- Messaging: added `Microsoft.Extensions.DependencyInjection.Abstractions` + `Logging.Abstractions`, referenced `Platform.Logging`. Wrote `DomainEventEnvelope<T>` (matching the Day 6 v1.1 envelope exactly), `IEventPublisher`, `IEventHandler<T>`, `InMemoryEventBus`.
- Both built clean (`0 Warning(s), 0 Error(s)`) before any tests were written.

## Steps 5-6 — Unit tests (EF Core InMemory, no infrastructure)

- `Platform.Persistence.Tests`: `Microsoft.EntityFrameworkCore.InMemory`, a `TestWidget`/`TestDbContext`/`TestWidgetRepository` stand-in. 6 tests, including `InRange` assertions on real (non-vacuous) `CreatedAt`/`UpdatedAt` timestamps. **95.45% coverage.**
- `Platform.Messaging.Tests`: `RecordingHandler` capturing received envelopes. 3 tests, including multi-handler fan-out. **90.47% coverage.**
- Both scoped with the `<Include>` coverage filter established Day 17.

## Step 7 — Wire into the real Api host; first real bug (scoped-service resolution)

Added `DiagnosticsDbContext`/`DiagnosticsWidget` and a `DiagnosticsWidgetCreatedHandler` (Development-only), wired `AddCustomsClearanceMessaging()`/`AddDbContext<DiagnosticsDbContext>()`, added `/diagnostics/widgets` POST/GET.

**First real run against `docker compose up -d postgres` + `dotnet run` locally:**
```bash
curl -s -X POST "http://localhost:5292/diagnostics/widgets?name=RealPostgresTest"
```
→ `500`, log showed: `InvalidOperationException: Cannot resolve scoped service 'IEnumerable<IEventHandler<...>>' from root provider.`

**Fix:** `InMemoryEventBus` now injects `IServiceScopeFactory`, creates a scope per `PublishAsync` call, instead of holding the root `IServiceProvider`. Unit test strengthened to register handlers Scoped with `BuildServiceProvider(validateScopes: true)`, so this class of bug is now caught automatically.

**Re-verified after the fix:**
```bash
curl -s -X POST "http://localhost:5293/diagnostics/widgets?name=RealPostgresTest"
# {"id":"28c24f56-...","name":"RealPostgresTest","createdAt":"...","eventId":"b37538b2-..."}
curl -s "http://localhost:5293/diagnostics/widgets/28c24f56-..."
# {"name":"RealPostgresTest",...}
grep -i "HANDLED\|Publishing event" api.log
# Publishing event DiagnosticsWidgetCreated ... to 1 handler(s). CorrelationId=be450e47-...
# HANDLED event DiagnosticsWidgetCreated ... CorrelationId=be450e47-...  (matches exactly)
```

**Independent verification, bypassing the API entirely:**
```bash
docker exec customsclerancenextgenapp-postgres-1 psql -U customsclearance -d customsclearance_dev -c 'SELECT * FROM "Widgets";'
```
→ rows genuinely present in PostgreSQL.

## Step 8 — Extend the CI smoke test to cover the real Postgres round-trip

Since the WebApplicationFactory-based Integration Tests job (Day 14) is in-process only and has no database, `build/scripts/smoke-test.sh` was extended to POST + GET a widget after the health check passes — this is the one CI job with a real Postgres container, so it's where Persistence gets real CI coverage.

## Step 9 — Second real bug: Postgres startup race crashes the Api

**First run of the updated smoke-test.sh** (piped through `tail -30`) appeared to succeed (`exit code 0` reported by the tool). Grepping for the expected round-trip output found nothing.

**Re-run with a direct, unpiped exit-code capture:**
```bash
bash build/scripts/smoke-test.sh > /tmp/smoke_day18_full.log 2>&1; echo "REAL EXIT CODE: $?"
# REAL EXIT CODE: 1
```
This was the third real bug (piping through `tail` silently discards the actual exit code — the pipeline's exit status is `tail`'s, not the script's). The log revealed the actual failure: Postgres restarted mid-init (a normal first-time-init pattern) exactly as the Api tried to connect, and the unhandled `Npgsql` exception crashed the entire process before `/health` could respond.

**Fixed at two levels:**
- `docker-compose.yml`: added a `pg_isready`-based healthcheck to the `postgres` service, and changed `api`'s `depends_on` to `condition: service_healthy` (not just container start).
- `Program.cs`: wrapped `EnsureCreatedAsync()` in a 5-attempt retry loop with 2s backoff, as defense in depth.

**Re-verified twice in a row, both with direct exit-code capture:**
```bash
bash build/scripts/smoke-test.sh > /tmp/smoke_day18_v2.log 2>&1; echo "REAL EXIT CODE: $?"   # 0
bash build/scripts/smoke-test.sh > /tmp/smoke_day18_v3.log 2>&1; echo "REAL EXIT CODE: $?"   # 0
```
Both showed `PostgreSQL round-trip verified: widget created and read back successfully.`

## Step 10 — Full pipeline re-run surfaces the fourth and fifth real bugs

Running the complete pipeline (build/lint/unit/integration) to confirm nothing regressed:
```bash
bash build/scripts/integration-test.sh
```
→ **6/6 failed**, ~56s each, `Npgsql.NpgsqlException: Failed to connect to 127.0.0.1:5432`.

**Root cause (Finding 4):** the new Program.cs Postgres-dependent startup code runs whenever `IsDevelopment()` is true — and `WebApplicationFactory` defaults its environment to "Development" regardless of any explicit `UseEnvironment` call, so every integration test (including ones with nothing to do with persistence) now tried to connect to a database that doesn't exist in that test host.

**First fix attempt** (`WebApplicationFactory.ConfigureWebHost` → `ConfigureAppConfiguration`, blanking `ConnectionStrings:DiagnosticsDb`) did not work — still failed identically. Diagnosed by adding a temporary `Console.WriteLine` to `Program.cs` and running with `dotnet test --logger "console;verbosity=detailed"` to actually see captured stdout:
```
[DEBUG] Resolved DiagnosticsDb connection string: 'Host=localhost;...' | env var raw: ''
```
This showed the override never took effect — `Program.cs`'s top-level statements read `builder.Configuration` synchronously, before `WebApplicationFactory`'s hooks apply for a minimal-hosting entry point.

**Second fix attempt:** set a process environment variable in a `TestApiFactory` static constructor instead. Still failed identically. The debug line explained why: `env var raw: ''` — meaning `Environment.SetEnvironmentVariable(name, "")` on Windows **deletes** the variable rather than setting it to empty.

**Actual fix:** use a single space `" "` instead of `""` — a real, present value that still satisfies `string.IsNullOrWhiteSpace`. Removed the temporary debug line from `Program.cs`.

**Re-ran:**
```bash
dotnet test tests/Integration/Api.IntegrationTests/...
```
→ New, faster failure (99ms, not 56s — confirming the connection guard now worked): `InvalidOperationException: Failure to infer one or more parameters` for the `dbContext` parameter of `/diagnostics/widgets`.

**Root cause (Finding 5):** ASP.NET Core's `RequestDelegateFactory` validates every minimal-API endpoint parameter is bindable **at route-mapping time**, not per-request — so mapping `/diagnostics/widgets` with an unregistered `DiagnosticsDbContext` parameter crashed the whole app at startup, including completely unrelated routes like `/health`.

**Fix:** moved the `MapPost`/`MapGet` calls for the widget endpoints inside the same `if (!string.IsNullOrWhiteSpace(connectionString))` guard controlling `DiagnosticsDbContext` registration.

**Final re-run:**
```bash
dotnet test tests/Integration/Api.IntegrationTests/CustomsClearance.Api.IntegrationTests.csproj
# Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6, Duration: 59 ms
```

## Step 11 — Full pipeline, final confirmation, all real exit codes

```bash
bash build/scripts/build.sh          # exit 0, 0 warnings
dotnet format --verify-no-changes --severity warn   # exit 0, clean
bash build/scripts/test.sh           # exit 0, 7 projects, 96.5% aggregate coverage
bash build/scripts/integration-test.sh  # exit 0, 6/6 passed, 59ms
bash build/scripts/smoke-test.sh     # exit 0, real Postgres round-trip verified
```

## Step 12 — Record the decision

**File added:** `adr/0012-persistence-and-messaging-libraries.md` — full technical detail on all five findings, their root causes, and fixes.

---

## Summary of Files Changed/Added This Day

- `src/Platform/Persistence/` — new (csproj, `AuditableEntity.cs`, `IRepository.cs`, `PlatformDbContext.cs`, `EfRepository.cs`)
- `src/Platform/Messaging/` — new (csproj, `DomainEventEnvelope.cs`, `IEventPublisher.cs`, `IEventHandler.cs`, `InMemoryEventBus.cs`, `ServiceCollectionExtensions.cs`)
- `tests/Unit/Platform.Persistence.Tests/`, `tests/Unit/Platform.Messaging.Tests/` — new
- `src/Api/Diagnostics/` — new (`DiagnosticsDbContext.cs`, `DiagnosticsWidgetCreatedHandler.cs`)
- `src/Api/Program.cs` — updated (persistence/messaging wired in, retry logic, endpoint-registration guard fix)
- `src/Api/appsettings.Development.json` — updated (DiagnosticsDb connection string)
- `src/Api/Dockerfile` — updated (2 new Platform projects copied into build context)
- `docker-compose.yml` — updated (postgres healthcheck, api depends_on condition, connection string env override)
- `build/scripts/smoke-test.sh` — updated (real Postgres round-trip check)
- `tests/Integration/Api.IntegrationTests/TestApiFactory.cs` — new (shared factory customization, env var override)
- `tests/Integration/Api.IntegrationTests/{Health,ErrorHandling,Secure}EndpointTests.cs` — updated (use `TestApiFactory`)
- `CustomsClearance.sln` — updated (4 new projects added)
- `adr/0012-persistence-and-messaging-libraries.md` — new
- `Phase-1_Architecture_Engineering_Prep/Day18_Persistence_and_Messaging_Libraries.docx` — new
- `Phase-1_Architecture_Engineering_Prep/Day18_Execution_Log.md` — this file

Not yet committed or pushed — pending review and commit-message request, per the established workflow.
