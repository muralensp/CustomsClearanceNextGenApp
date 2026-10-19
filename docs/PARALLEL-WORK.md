# Working in parallel (people or agents)

How to run several changes at once without them colliding. Written for the multi-agent pilot
(see ADR-0035), but it applies equally to two humans on two branches.

## 1. Adding a capability to the Api host

Do **not** edit `src/Api/Program.cs`. Add one file, `src/Api/Modules/<Name>Module.cs`,
implementing `IApiModule`. It is discovered automatically (`ApiModuleCatalog`).

A module owns, in one place:

| Hook | What goes in it |
|---|---|
| `ConfigureServices` | its DbContext, handlers, other services - only when its connection string is present |
| `RegisterEventTypes` | its domain-event payload types (used only when RabbitMQ is configured) |
| `InitializeAsync` | Development-only: `DatabaseBootstrap.WithRetryAsync` + `EnsureSchemaAsync`, then any seeding |
| `MapEndpoints` | its routes, guarded by **the same condition** as `ConfigureServices` |

Rules that came from real failures - keep them:

- **One condition, three places.** Registration, schema bootstrap and route mapping must agree on whether
  the module is enabled (almost always: its `ConnectionStrings:<X>Db` is present). Mapping a route whose
  DbContext is not registered crashes the *whole* app at start-up (Day 18).
- **`Order`** only matters where start-up order matters. `Diagnostics` (10) must stay first: it creates
  the physical database every later module adds tables to (Day 23). Leave gaps (10, 20, 30...) so a new
  module slots in without renumbering. Orders and names must be unique - `ApiModuleCatalogTests` enforces it.
- Infrastructure-free tests (ADR-0009): the module must do nothing when its connection string is blank.
- **Update `ApiModuleCatalogTests`** in the same change: add the module to the expected-module list, and
  (if it maps routes) to the "routes are not mapped when the connection string is blank" cases. Without
  this the integration tests fail, which is the test doing its job, not a bug in discovery.
- Also blank the new connection string in `TestApiFactory` (see section 2).

## 2. Who edits what

After this refactor `Program.cs` is no longer a hot file. These still are - an edit here from a
parallel branch is expected to conflict, so **one owner merges them** (the orchestrator: the main
session, or whoever integrates). Branches may still *propose* edits; they resolve at merge time.

| File | Why it is shared | Rule |
|---|---|---|
| `src/Api/Program.cs` | core cross-cutting wiring only | Edit only for host-wide concerns (logging, auth, tracing, middleware order). Never for a capability. |
| `CustomsClearance.sln` | every new project adds lines | Additive. Orchestrator resolves. |
| `src/Api/CustomsClearance.Api.csproj` | a `ProjectReference` per new platform library | Additive. Orchestrator resolves. |
| `src/Api/Dockerfile` (and `src/Gateway/Dockerfile`) | a `COPY` per new platform library | Additive. Orchestrator resolves. |
| `docker-compose.yml` | the `api` service's `ConnectionStrings__*` env list; any new service | Additive. Never hard-code a host port - use `${NAME_PORT:-default}` (section 3). |
| `src/Api/appsettings.Development.json` | one `ConnectionStrings` entry per context | Additive. |
| `tests/Integration/Api.IntegrationTests/TestApiFactory.cs` | must blank each new connection string | Additive. A new context that is not blanked there breaks every integration test. |
| `build/scripts/smoke-test.sh`, `e2e-smoke-test.sh` | a check/stage per capability | Append at the end of the relevant section. |
| `.github/workflows/ci.yml` + `build/azure-pipelines.yml` | CI changes | Always edit both together (ADR-0005). |
| `CLAUDE.md` | a log entry per piece of work | Orchestrator writes it at integration, from the branch's execution log - not the branch itself. |
| `adr/NNNN-*.md` | numbered sequentially | **Reserve the number up front** (orchestrator assigns it in the task brief). Two branches both taking "the next number" is a certain collision. |

Everything else - a module file, its `src/Platform/<Lib>/` project, its tests, its own ADR and execution
log - is owned by the one branch that created it. Two workers must never both edit a non-shared file.

## 3. Running a stack per worker (no port / name collisions)

A git worktree isolates *code*. It does not isolate Docker: two copies of `docker compose up` fight over
host ports (5432, 8080, 8025, ...) and over container, network and volume names. Each worker runs in its
own numbered slot:

```bash
build/scripts/agent-env.sh 1                # prints the export lines for slot 1 (use 1-9; 0 = default stack)
eval "$(build/scripts/agent-env.sh 1)"     # apply them to this shell
bash build/scripts/e2e-smoke-test.sh        # now talks to slot 1's own gateway/mailpit
docker compose ps                           # this slot's containers only
```

Some restricted harnesses block `eval`/`source`. In that case run the first command, then put the printed
`export` values in front of the same command line as `docker compose` and the scripts (the variables
must be set in the same shell invocation as the command that uses them).

A fresh `docker compose up -d --build` can exit 1 on the known RabbitMQ first-boot race (ADR-0028); a
plain second `docker compose up -d` is the expected recovery.

Slot N gets compose project `cc-slot-N` (so its own containers, network and named volumes), image tags
prefixed `cc-slot-N`, and host ports offset by N x 1000 (SMTP by N). Nothing is overridden for slot 0,
and CI is untouched. Give each worker its own `E2E_EVIDENCE_FILE` if more than one runs the E2E script.

Limits - be honest about them:
- **CPU and memory.** A full stack is ~12 containers plus a .NET build. Two or three at once is fine on a
  workstation; many is not. This is the real ceiling on parallel full-stack runs.
- **Unit and integration tests need no stack** (ADR-0009) and parallelise freely.
- `build/scripts/contract-test.sh` still targets `localhost:8080` (slot 0 only).

## 4. Verification evidence

"Done" means proven against the running system with unpiped exit codes (CLAUDE.md). For a parallel
worker that means the branch hands back *files and numbers*, not a summary: the commands it ran, their
exit codes, the test and coverage output, and the E2E evidence file for its slot. The integrator
re-runs the checks; it does not take a statement like "all tests passed" on trust.
