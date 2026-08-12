# ADR-0012: Persistence & Messaging Platform Libraries

**Status:** Accepted
**Date:** 2026-08-12
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Day 18 of the Day-Wise Plan calls for two final Platform libraries: shared repository base classes/ORM setup, and an event-bus publish/subscribe wrapper. Per ADR-0001, EF Core is the sole data-access layer (PostgreSQL initially, SQL-Server-portable); per Day 5/6, domain events use a unified envelope. This ADR implements both.

## Decision

1. **`CustomsClearance.Platform.Persistence`** — `AuditableEntity<TId>` (base entity with `CreatedAt`/`UpdatedAt`), `IRepository<TEntity, TId>` (generic CRUD port), `EfRepository<TEntity, TId>` (generic EF Core implementation — deliberately does **not** call `SaveChangesAsync` itself; Unit of Work stays with the `DbContext`, called once by the Application layer after all repository operations for a use case complete), and `PlatformDbContext` (base `DbContext` every context's Infrastructure project derives from, auto-stamping `CreatedAt`/`UpdatedAt` in its `SaveChangesAsync` override).
2. **`CustomsClearance.Platform.Messaging`** — `DomainEventEnvelope<TPayload>` (the exact envelope from `Day6_API_Standards_v1.1.docx` Section 3.1: `EventId`, `EventType`, `EventVersion`, `OccurredAt`, `CorrelationId`, `Source`, `Payload`), `IEventPublisher`/`IEventHandler<TPayload>` (pub/sub ports), and `InMemoryEventBus` (a same-process, functional placeholder implementation — real distributed broker selection is a Phase 0 infrastructure decision, same pattern as `Platform.Audit`'s `ConsoleAuditWriter`, Day 17).

Both are ports-and-adapters designs consistent with `adr/0006`'s Clean Architecture adaptation: these Platform libraries provide the shared building blocks; a specific bounded context's own Domain/Application project would define its own narrower interfaces (e.g. `IDeclarationRepository`) on top of them.

## Verification Against the Real Host — Including Two Real Bugs Found and Fixed

A new `DiagnosticsDbContext`/`DiagnosticsWidget` (Development-only, mirroring the pattern from Days 16-17) and a `/diagnostics/widgets` POST/GET pair were added to `src/Api`, proving both libraries together: creating a widget writes to real PostgreSQL *and* publishes an event that a real registered handler receives.

| Check | Result |
|---|---|
| `docker exec ... psql -c 'SELECT * FROM "Widgets"'` (independent of the API's own read-back) | Rows genuinely persisted in PostgreSQL, confirmed via `psql` directly against the container, not just trusting the API's own response |
| Event publish → handler log line | `HANDLED event DiagnosticsWidgetCreated (...) for widget ... CorrelationId=<id>` — matched the response's `X-Correlation-Id` exactly |
| Unit tests (`Platform.Persistence.Tests`, EF Core InMemory provider) | 6 tests, 95.45%+ coverage, including genuine (non-vacuous) `CreatedAt`/`UpdatedAt` timestamp assertions |
| Unit tests (`Platform.Messaging.Tests`) | 3 tests, 90%+ coverage, including multi-handler fan-out |
| `docker compose` smoke test, extended to a real round-trip check | POST + GET `/diagnostics/widgets`, verified with a **real exit-code check** (`bash script.sh; echo $?`), not a piped `| tail` that silently discards the actual exit status — a distinction that mattered directly, see Finding 3 below |

### Finding 1: `InMemoryEventBus` couldn't resolve Scoped handlers from a Singleton's root provider

First real end-to-end run against the actual Api host threw `InvalidOperationException: Cannot resolve scoped service ... from root provider`. `InMemoryEventBus` is registered Singleton (correct — a bus should outlive a request), but `IEventHandler<T>` implementations are typically Scoped (they may depend on a `DbContext`). Resolving a Scoped service from the Singleton's captured root `IServiceProvider` throws by design (DI container safety check). **Fixed** by injecting `IServiceScopeFactory` instead and creating a scope per `PublishAsync` call. The unit test was also strengthened to register handlers as Scoped with `BuildServiceProvider(validateScopes: true)`, so this exact class of bug would be caught by the unit test suite in the future, not only by a manual end-to-end run.

### Finding 2: A Postgres startup race crashed the whole Api process

`docker-compose`'s `depends_on: - postgres` only waits for the *container* to start, not for Postgres to finish its first-time initialization (which involves an internal restart cycle) and actually accept connections. The Api's `EnsureCreatedAsync()` call hit this race and threw an unhandled exception, crashing the entire process before `/health` could even respond — confirmed by an actual smoke-test failure (silently masked once, by piping through `tail`, until re-run with a direct exit-code check). **Fixed at two levels**: (1) `docker-compose.yml`'s `postgres` service now has a `pg_isready`-based healthcheck, and `api` depends on `condition: service_healthy`, not just container start; (2) `Program.cs`'s `EnsureCreatedAsync` call is wrapped in a 5-attempt retry with a 2-second backoff, as defense in depth against any remaining brief connection hiccup.

### Finding 3: Piping a script through `tail` silently discards its real exit code

The first "successful" smoke-test run (`bash smoke-test.sh 2>&1 | tail -30`) reported exit code 0 — but that was `tail`'s exit code, not the script's (a pipeline's exit status is its last command's, unless the shell has `pipefail` set, which the interactive command did not). Re-running with `bash smoke-test.sh > file.log 2>&1; echo $?` (no pipe) revealed the script had actually failed with exit 1 the whole time. This is now a standing lesson: **never trust a piped command's apparent success** — capture the real exit code directly, especially for scripts with explicit `exit 1` paths.

### Finding 4: Config overrides don't reliably reach minimal-hosting `Program.cs` in time

To keep the WebApplicationFactory-based Integration Tests layer infrastructure-free (per `adr/0009`), the test host needs to skip the DB-dependent startup path. The first attempt (`WebApplicationFactory.ConfigureWebHost` → `ConfigureAppConfiguration`) did not work: `Program.cs`'s top-level statements call `builder.Configuration.GetConnectionString(...)` synchronously while building `WebApplicationBuilder` itself, before `WebApplicationFactory`'s hooks get a chance to apply for a minimal-hosting (top-level `Program.cs`) entry point. Switched to setting a process-level environment variable in `TestApiFactory`'s static constructor (read by `WebApplication.CreateBuilder`'s default `AddEnvironmentVariables()` provider at the same `Build()` time) — but the **first version of that fix also silently failed**, because `Environment.SetEnvironmentVariable(name, "")` on Windows deletes the variable rather than setting it to an empty value. Diagnosed by temporarily adding a debug `Console.WriteLine` to `Program.cs` and inspecting `dotnet test`'s captured output (only visible with `--logger "console;verbosity=detailed"`) — confirmed the override was a no-op. Fixed by using a single space (`" "`) instead of `""`: a real, present value that still satisfies `string.IsNullOrWhiteSpace`.

### Finding 5: Minimal API route mapping fails at startup, not per-request, if a parameter can't be resolved as a service

Once Finding 4's fix correctly blanked the connection string, a *new* failure appeared: `Failure to infer one or more parameters` for the `dbContext` parameter of `/diagnostics/widgets`. ASP.NET Core's `RequestDelegateFactory` validates that every minimal-API endpoint parameter is bindable **at the time the route is mapped**, not lazily per-request — so mapping `/diagnostics/widgets` with an unregistered `DiagnosticsDbContext` parameter crashed the *entire* app at startup, including completely unrelated endpoints like `/health`. **Fixed** by moving the `MapPost`/`MapGet` calls for the widget endpoints inside the same `if (!string.IsNullOrWhiteSpace(connectionString))` guard that controls whether `DiagnosticsDbContext` is registered — the endpoints simply don't exist when persistence isn't configured, rather than existing and failing.

## Consequences

- Every future bounded context's Infrastructure project should derive its `DbContext` from `PlatformDbContext` and expose repositories via `IRepository<TEntity, TId>`/`EfRepository<TEntity, TId>`, not hand-roll EF Core wiring.
- Every future publisher of a domain/integration event should use `DomainEventEnvelope<TPayload>.Create()` and `IEventPublisher`, not a bespoke event mechanism.
- **`InMemoryEventBus` is not the production message bus.** It does not survive a process restart, cross process/service boundaries, or provide retry/dead-letter semantics. A real broker (Kafka/RabbitMQ/Azure Service Bus, per the PRD and Day 5 event catalog) is a Phase 0 infrastructure decision, registered by overriding `AddCustomsClearanceMessaging()`'s DI registration when that time comes — the envelope shape and `IEventHandler` contract used by application code do not change.
- Any future endpoint depending on a conditionally-registered service must have its **route mapping**, not just its handler logic, guarded by the same condition — Finding 5 is a general minimal-API lesson, not specific to persistence.
- Diagnosing "why doesn't my test host see this config/env override" for minimal-hosting `Program.cs` apps should start from Finding 4's two root causes (hook timing, and the Windows empty-string-deletes-the-variable behavior) rather than re-deriving them from scratch.

## Related

[[ADR-0001]] (stack — EF Core/PostgreSQL, the reason `Platform.Persistence` exists in this shape). [[ADR-0006]] (Clean Architecture — ports/adapters pattern both libraries follow). [[ADR-0007]] (coverage gate — both libraries' test projects use the `<Include>` scoping fix from `adr/0011`). [[ADR-0008]] (CD pipeline — `docker-compose.yml`'s healthcheck fix). [[ADR-0009]] (test automation layering — why the Integration Tests layer must stay infrastructure-free, and the `TestApiFactory` fix that makes it so). [[ADR-0010]], [[ADR-0011]] (Logging/ErrorHandling, Security/Audit — the same "prove it against the real host, not just unit tests" discipline, continued here with two additional real bugs found by doing so).
