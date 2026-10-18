# ADR-0035: Api host modules and parallel-work isolation

**Status:** Accepted
**Date:** 2026-10-18
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

The user asked how to speed up development with multiple agents. The proposal (and an external review of
it, `Multi_Agent_Approach.docx`) agreed on one precondition before any parallel work: reduce shared-file
conflicts and isolate the runtime, because git worktrees isolate only code.

Looking at Days 36-40, every day edited the same files. The worst was `src/Api/Program.cs`: 863 lines, in
which each capability added (a) a service-registration block, (b) a seven-times-copied "create my tables"
retry block, (c) a route-mapping guard, and in places (d) dev-only diagnostic endpoints. Two parallel
changes could not both edit it without conflicting. Separately, `docker-compose.yml` published fixed host
ports (5432, 8080, 8025, ...), so a second full stack on the same machine could not start, and the smoke
scripts hard-coded `localhost:8080`.

This is the "Foundation" stage of the adoption plan. It changes structure only; no behaviour.

## Decision

1. **`IApiModule`** (`src/Api/Modules/`) - one self-contained capability of the Api host, with hooks to
   register services, register event types, bootstrap its schema (Development only) and map routes.
   Seven modules were extracted with their code moved, not rewritten: Diagnostics (10), Identity (20),
   RulesEngine (30), Configuration (40), Notifications (50), Audit (60), ReferenceData (70).
2. **Discovery by assembly scan** (`ApiModuleCatalog`), not an explicit list. A list in `Program.cs` would
   be the very line two parallel changes both edit. Scanning means a new capability is a new file.
3. **`DatabaseBootstrap`** - the retry-with-backoff and EnsureCreated-then-CreateTables fallback that was
   copied seven times, once. The Day 18 and Day 23 findings that shaped it are kept as comments there.
4. **Each module decides for itself whether it is enabled** and applies the same condition to
   registration, schema bootstrap and route mapping, so those can never disagree (Day 18 map-time
   bindability lesson). Every hook is called for every module; a disabled module does nothing.
5. **Compose is parameterised, defaults unchanged.** All host ports are `${NAME_PORT:-default}`, image
   tags use `${IMAGE_PREFIX:-customsclearance}`, and container/network/volume names already derive from
   the compose project name (no `container_name` anywhere). `build/scripts/agent-env.sh <slot>` prints
   the variables for a numbered slot (project `cc-slot-N`, ports offset N x 1000, SMTP offset N).
   `smoke-test.sh` and `e2e-smoke-test.sh` honour `GATEWAY_PORT` / `MAILPIT_UI_PORT`.
6. **`docs/PARALLEL-WORK.md`** records which files remain shared, who owns each, and the rules for
   adding a module and for reserving ADR numbers up front.

## Verification

All with unpiped exit codes, on the real stack:

- Build 0 errors (only the known NU1902); `dotnet format --verify-no-changes` exit 0 (it caught my first
  draft: private fields need the `_` prefix); `dotnet test CustomsClearance.sln` exit 0 including all
  coverage gates. Integration tests went 6 -> 14 (`ApiModuleCatalogTests`: expected module set, unique
  ascending orders, Diagnostics first, five module routes unmapped when their connection string is blank).
- **Behaviour unchanged**: the Day 40 E2E script passed 20/20 (exit 0) against the refactored Api, and the
  unmodified CI `smoke-test.sh` passed (exit 0) on the default stack.
- **Isolation proven, not assumed**: the E2E script passed 20/20 (exit 0) on slot 0 (default, port 8080)
  and slot 1 (`cc-slot-1`, gateway 9080) started within 2 seconds of each other, each with its own evidence
  file; both tore down cleanly, leaving no containers. The two image builds overlapped; the test phases
  overlapped only partly, so this shows the stacks coexist, not that they were under sustained load.
- `agent-env.sh`: 110 ports (11 variables x slots 1-9, plus the defaults) all distinct.

## Alternatives considered

- **Explicit module list in `Program.cs`.** More readable, but it is a shared line; rejected for the
  stated goal. Costs nothing in `Program.cs`, at the price of a little reflection (`Activator`), which is
  covered by `ApiModuleCatalogTests`.
- **A source generator or `IStartupFilter` per module.** More machinery than a one-method-per-hook
  interface warrants at this size.
- **Stride 100 for slot ports.** Rejected: Loki (3100) and Tempo (3200) default ports are 100 apart, so
  slot 1's Loki would take slot 0's Tempo port. A script check across slots 0-9 found 110/110 distinct
  ports with the chosen scheme (and 3 collisions before the SMTP port was changed to step by 1).

## Consequences

- `Program.cs` is ~125 lines of host-wide wiring. A new capability does not touch it.
- **Still shared, deliberately not solved here** (listed with owners in `docs/PARALLEL-WORK.md`): the
  `.sln`, `Api.csproj` `ProjectReference`s, `Api/Dockerfile` `COPY` lines, the `api` service's
  `ConnectionStrings__*` env list, `appsettings.Development.json`, `TestApiFactory`'s blanking list,
  `CLAUDE.md`, and ADR numbering. These are additive one-liners and git resolves most; one owner merges
  them. Removing them (e.g. `TestApiFactory` blanking every `ConnectionStrings__*` dynamically) is a
  candidate follow-up if the pilot shows they hurt.
- Reflection-based discovery makes the module set implicit. The tests assert the expected set, unique
  names/orders, and that `Diagnostics` runs first, so an accidental change fails loudly.
- Port isolation does not remove the real ceiling: CPU/memory. A full stack is about a dozen containers
  plus a .NET build.
- `build/scripts/contract-test.sh` still targets `localhost:8080` (slot 0 only).
- The Gateway project is unchanged.

## Related

[[ADR-0003]] (modular monolith), [[ADR-0005]] (CI lockstep), [[ADR-0009]] (test layering - modules do
nothing without their connection string), [[ADR-0028]] (compose restart/retry), [[ADR-0034]] (the E2E
script run against two stacks as the proof).
