# Runbook: add a new capability (module) to the Api host

"Module" here means an `IApiModule` inside the single `src/Api` host (ADR-0035) backed by a platform library
under `src/Platform/<Name>/`. Phase 0 has no real bounded contexts, so this is how every Phase 0 capability
was added (Identity, Rules, Configuration, Notifications, Audit, Reference data).

For a real **bounded context** (Declaration, Compliance, ...), use `templates/BoundedContextTemplate/` for
the project split first (ADR-0006, ADR-0015) and then wire it in with steps 3-8 below. A new capability is
**not** a new service or container: this is a modular monolith (ADR-0003).

Parallel-work rules (which files are shared, who merges them): [docs/PARALLEL-WORK.md](../PARALLEL-WORK.md).

## How this runbook was verified

Followed literally in an isolated worktree with a throwaway module named `Scratch` (entity, `DbContext`,
service, `/api/scratch` endpoints, unit-test project): solution build, unit tests, integration tests and a
live request through the Gateway against real Postgres all worked, then every edit was removed and
`git status` showed nothing left over. Details and exit codes: Day 44 execution log. Items marked
*(not exercised)* below come from the code and ADRs but were not run.

## Checklist

Replace `Foo` with the capability name.

### 1. Decide first

- Connection-string name: `FooDb` (`ConnectionStrings:FooDb`). A module is **enabled iff this is non-blank**.
- `Order`: an unused number. Existing: Diagnostics 10, Identity 20, RulesEngine 30, Configuration 40,
  Notifications 50, Audit 60, ReferenceData 70. Use 80, 90, ... Names and orders must be unique. Diagnostics
  must stay first (it creates the physical database).
- Coverage tier (ADR-0007): **95%** line threshold if it holds core business/rules/security logic, **80%** if
  it is CRUD/master-data plumbing. Write it down in the test csproj comment.
- Reserve an ADR number with the orchestrator before starting if other work is in flight (ADR numbers
  collide otherwise).

### 2. Platform library: `src/Platform/Foo/`

Copy the shape of `src/Platform/ReferenceData/`:

- `CustomsClearance.Platform.Foo.csproj`: `net8.0`, `Microsoft.EntityFrameworkCore` 8.0.10, a
  `ProjectReference` to `..\Persistence\CustomsClearance.Platform.Persistence.csproj`. Never reference
  `src/Api` or another capability's library.
- Entities derive from `AuditableEntity<TId>` (CreatedAt/UpdatedAt are stamped automatically).
- `FooDbContext : PlatformDbContext` with `DbContextOptions<FooDbContext>`.
- Business logic in services/classes that take the `DbContext`/repositories; keep endpoints thin. Do **not**
  call `SaveChangesAsync` inside a generic repository; the use case calls it once (ADR-0012).
- Avoid provider-specific SQL (ADR-0001). Return DTO projections from endpoints, never entity graphs
  (JSON cycles, Day 31).
- Register DI in a `ServiceCollectionExtensions.AddCustomsClearanceFoo(...)` if there is more than a DbContext.

### 3. Api module: `src/Api/Modules/FooModule.cs`

One new file implementing `IApiModule`; nothing is edited in `Program.cs` (discovery is by assembly scan).
Pattern (see `ReferenceDataModule.cs`, `RulesModule.cs`):

```csharp
public sealed class FooModule : IApiModule
{
    private const string _connectionName = "FooDb";      // private fields need a leading underscore (.editorconfig)
    public string Name => "Foo";
    public int Order => 80;

    public void ConfigureServices(WebApplicationBuilder builder)
    {
        var cs = builder.Configuration.GetConnectionString(_connectionName);
        if (!string.IsNullOrWhiteSpace(cs))
            builder.Services.AddDbContext<FooDbContext>(o => o.UseNpgsql(cs));
    }

    public async Task InitializeAsync(WebApplication app)          // Development only
    {
        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(_connectionName))) return;
        await DatabaseBootstrap.WithRetryAsync(app, _connectionName, async () =>
        {
            using var scope = app.Services.CreateScope();
            await DatabaseBootstrap.EnsureSchemaAsync(scope.ServiceProvider.GetRequiredService<FooDbContext>());
        });
    }

    public void MapEndpoints(WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(_connectionName))) return;
        var group = app.MapGroup("/api/foo").RequireAuthorization();   // or PlatformPolicies.RequireAdmin etc.
        // group.MapGet(...), group.MapPost(...)
    }
}
```

The rule that matters: **the same condition in three places** (registration, schema bootstrap, route
mapping). Mapping a route whose DbContext is not registered crashes the whole app at start-up, including
unrelated routes. Choose authorization explicitly for every route group (policies in
`Platform.Security.PlatformPolicies`). Put endpoint code in `src/Api/Foo/FooEndpoints.cs` once it grows.
Dev-only diagnostic endpoints go behind `app.Environment.IsDevelopment()`.

**If the module emits or handles domain events** *(not exercised in the Scratch run)*: implement
`IEventHandler<TPayload>` in the library, register the handler in DI, and override
`RegisterEventTypes(EventTypeRegistry registry)` with `registry.Register<TPayload>("EventName")`
(see `NotificationsModule`, `DiagnosticsModule`). Publish via `IEventPublisher`.

**If it audits changes** *(not exercised)*: write `IAuditWriter` events as `ConfigAdmin/ConfigAudit.cs`
does (ADR-0032).

### 4. Shared-file wiring (each is additive; all are the "shared files" in PARALLEL-WORK.md section 2)

| File | Edit | Failure if you forget |
|---|---|---|
| `CustomsClearance.sln` | `dotnet sln CustomsClearance.sln add src/Platform/Foo/CustomsClearance.Platform.Foo.csproj tests/Unit/Platform.Foo.Tests/CustomsClearance.Platform.Foo.Tests.csproj` | project not built/tested by `dotnet build`/CI |
| `src/Api/CustomsClearance.Api.csproj` | `dotnet add src/Api/CustomsClearance.Api.csproj reference src/Platform/Foo/CustomsClearance.Platform.Foo.csproj` | Api does not compile |
| `src/Api/Dockerfile` | two lines: the `.csproj` `COPY` before `dotnet restore`, and the folder `COPY` after it (mirror the `ReferenceData` lines) | Docker build fails (`dotnet restore`/`publish` cannot find the project); `dotnet build` on the host still works, so this is only caught by a container build |
| `docker-compose.yml` | in the `api` service `environment:` add `ConnectionStrings__FooDb=Host=postgres;Port=5432;Database=customsclearance_dev;Username=customsclearance;Password=devlocalonly` | module silently disabled in the container: no table, no routes (404) |
| `src/Api/appsettings.Development.json` | add `"FooDb": "Host=localhost;Port=5432;Database=customsclearance_dev;Username=customsclearance;Password=devlocalonly"` under `ConnectionStrings` | module disabled for `dotnet run` outside Docker |
| `tests/Integration/Api.IntegrationTests/TestApiFactory.cs` | add `Environment.SetEnvironmentVariable("ConnectionStrings__FooDb", " ");` (a single space; `""` deletes the variable on Windows) | integration tests try to reach a real database: slow failures across **every** integration test |
| `tests/Integration/Api.IntegrationTests/ApiModuleCatalogTests.cs` | add `"Foo"` to `_expectedModules`; add `[InlineData("/api/foo")]` to the "routes not mapped when connection string absent" theory | `Discover_FindsEveryExistingCapability` fails (this happened in the Scratch run, as intended) |

`src/Gateway/Dockerfile` needs no change for a library only the Api uses. The Gateway proxies everything
through its catch-all route; add a Gateway route/policy only if the new route needs Gateway-level auth
(`src/Gateway/appsettings.json`, see ADR-0019).

All the edits above were made in the Scratch run: they compile, tests pass, and the module came up in the
container and answered `401` without a token and `200` with one. The "failure if you forget" column is
derived from how each file works; only the `ApiModuleCatalogTests` failure was actually observed (the
Dockerfile, compose and `TestApiFactory` omissions were not deliberately triggered).

### 5. Tests

- Create `tests/Unit/Platform.Foo.Tests/` by copying `tests/Unit/Platform.ReferenceData.Tests/*.csproj`:
  xUnit, `coverlet.msbuild` (never `coverlet.collector`, they conflict), `Microsoft.EntityFrameworkCore.InMemory`,
  `<Include>[CustomsClearance.Platform.Foo]*</Include>` so coverage is scoped to your assembly, and
  `<Threshold>` 80 or 95 per step 1.
- Test the library with the InMemory provider; test pure logic directly. No mocking library is used anywhere
  in this codebase (consistent pattern). Mark genuinely un-unit-testable I/O adapters
  `[ExcludeFromCodeCoverage]` with a written justification (as the RabbitMQ classes do) rather than
  hand-rolling fakes, and prove them live instead.
- Run: `dotnet test tests/Unit/Platform.Foo.Tests` (this enforces the threshold), then
  `bash build/scripts/integration-test.sh`.

### 6. Verify against the real system (CLAUDE.md: unit tests alone are not "done")

```bash
eval "$(bash build/scripts/agent-env.sh <your slot>)"      # or default slot 0
docker compose up -d --build        # re-run `docker compose up -d` if rabbitmq first-boot race trips it
# log in as a dev persona (RUN-LOCALLY.md), then call your routes through the Gateway:
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:8080/api/foo                         # expect 401
curl -s http://localhost:8080/api/foo -H "Authorization: Bearer $TOKEN"                        # expect 200
```

Also check `docker compose logs api` for your table's `CREATE TABLE` and confirm with `psql` if the
module is meant to own tables. Capture exit codes unpiped.

### 7. Smoke and E2E coverage

- `build/scripts/smoke-test.sh`: append a check for your capability at the end of the relevant section
  (it runs in CI's containerize job). *(not exercised in the Scratch run)*
- `build/scripts/e2e-smoke-test.sh`: add a stage only if the capability is part of the cross-system chain;
  keep stage numbering and the evidence file consistent and keep "stop at first failure". *(not exercised)*
- Both scripts must keep working at any slot: use `${GATEWAY_PORT:-8080}`, never a hard-coded port.

### 8. Gates and documentation

1. `dotnet build CustomsClearance.sln` and `dotnet test` on your projects; full-solution unit tests via
   `bash build/scripts/test.sh` and integration tests via `bash build/scripts/integration-test.sh`.
2. `dotnet format style CustomsClearance.sln --verify-no-changes` and `... analyzers ...` (or
   `bash build/scripts/lint.sh`; do not pass `--nologo`).
3. Gate 1 review, `docs/quality-gates/Gate1_Architecture_Review.md` ("run at the start of implementing any
   new bounded context module"), for a real bounded context.
4. An ADR in `adr/` (`adr/template.md`) for any real decision, and an `Execution_Log.md` under
   `Phase-1_Architecture_Engineering_Prep/` with commands, exit codes and evidence.
5. CLAUDE.md gets a one-line table row at integration, written by the orchestrator, not by the branch.
6. Update the module table in `docs/Architecture/README.md` and, if a container or flow changed, `docs/C4/README.md`.

## Removing a module (what the Scratch run did)

Reverse the steps: delete the module file, library, test project; `dotnet sln ... remove`; remove the
project reference; remove the Dockerfile lines, compose variable, appsettings entry, `TestApiFactory` line
and the catalog-test entries; `docker compose down -v` (tables persist in the volume otherwise). `git status`
should then show no leftovers. The module's data is only ever in the dev database, so removing the volume
removes it.
