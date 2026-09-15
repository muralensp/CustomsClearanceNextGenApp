# ADR-0015: Local Dev Environment & Bounded-Context Template (Day 21)

**Status:** Accepted
**Date:** 2026-09-15
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Day 21 of the Day-Wise Plan ("Environment & Containerization") calls for: set up local/dev
Kubernetes (or Docker Compose, for solo-scale dev) and a base service template project, with
deliverable "a running hello-world service in a container." This is also the point ADR-0008
explicitly deferred the cloud/hosting-target decision to ("a real, reachable, always-on dev
environment is a Phase 0, Day 21 decision... deliberately not made here").

Two things about the Day-Wise Plan's Day 21 language don't map onto this project literally and
needed a decision before proceeding, the same way ADR-0006 adapted "textbook" Clean Architecture
rather than applying it unexamined:

1. **Orchestration choice.** The plan itself names Docker Compose as the fit "for solo-scale dev."
   The user confirmed Docker Compose over local Kubernetes (kind/minikube) — no orchestration
   complexity is justified yet, since zero bounded contexts have real code to orchestrate.
2. **"Base service template project."** The Day-Wise Plan's wording (and Days 22-25's "API
   Gateway," "Identity service," "Event bus" as separate infrastructure pieces) reads as written
   for a microservices topology. This project committed to a **Modular Monolith** (ADR-0003): one
   deployable `Api` host, not one container per bounded context. A literal "base service template"
   (a whole new containerized service scaffold) doesn't fit that decision. What Phase 0/3 will
   actually need, once the 17 empty `src/{Context}/` folders start getting real code, is a
   **bounded-context project template** matching ADR-0006's `Context.csproj` +
   `Context.Infrastructure.csproj` split — not a new container per context.
3. **Cloud/hosting target.** The user confirmed Day 21 stays scoped to local dev only. Staging/
   Production rows in ADR-0008's promotion table remain "Not yet implemented — awaits a real
   hosting target," deferred again rather than decided here.

## Decision

1. **Docker Compose remains the local/dev environment**, unchanged in shape from ADR-0008 (Day
   13)/Day 18: `docker-compose.yml` at the repo root, `api` + `postgres` services, `pg_isready`
   healthcheck gating startup order. No new orchestration tooling introduced.
2. **The Day 21 "hello world in a container" deliverable is satisfied by the existing `Api` host**,
   not a new scaffold project — `/health` (added pre-Day-13) already serves this purpose. Re-proven
   today end-to-end against a freshly built image (not assumed from Day 13/18's prior runs):
   `docker compose up -d --build` → `GET /health` → `200 {"status":"Healthy",...}` → `POST
   /diagnostics/widgets?name=day21-check` → `200`, with container logs confirming a real `CREATE
   TABLE "Widgets"` DDL statement executed against the `postgres` service (not a mock/in-memory
   fallback).
3. **"Base service template" is reinterpreted as a bounded-context project template**, added at
   `templates/BoundedContextTemplate/`: two `.csproj` template files (`Context.csproj` — no EF Core
   reference, matching ADR-0006's Domain+Application merge; `Context.Infrastructure.csproj` — EF
   Core + Npgsql references, matching `Platform.Persistence`'s existing package versions) plus a
   `README.md` explaining the placeholder-rename steps and pointing back to ADR-0006/ADR-0003.
   Copied into a `src/{Context}/` folder and renamed when that context's real build begins
   (earliest currently scoped: Phase 3+, per the Day-Wise Plan's later phases — none of Days 21-25
   populate a bounded context).
4. **No cloud provider or hosting target is chosen.** ADR-0008's Staging/Production rows are left
   as-is; this ADR does not attempt to fill them in.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Local Kubernetes (kind/minikube) | Closer to eventual production topology | Real setup/maintenance overhead; nothing exists yet to orchestrate (all 17 contexts empty) | User declined — matches the project's repeated pattern of not building infrastructure ahead of a real need (see ADR-0008's "not a persistently hosted environment" boundary) |
| Literal "new microservice scaffold" template (one container per future bounded context) | Matches the Day-Wise Plan's literal wording | Contradicts ADR-0003's Modular Monolith decision; would require walking back Days 22-25's gateway/identity/event-bus language too | Rejected — an adaptation, documented explicitly here, is preferable to silently diverging from the plan without a record (same reasoning as ADR-0006) |
| Decide cloud/hosting target now, fill in ADR-0008's Staging/Production rows | Removes a recurring "deferred" note | No Staging/Production need exists yet; a premature commitment (vendor, region, cost) with nothing to deploy | User declined — stays deferred until a real hosting need exists |

## Consequences

- `templates/BoundedContextTemplate/` is unused scaffolding until Phase 3+ (or whenever a bounded
  context's real build begins) — expected, not a gap; it exists so that first real context doesn't
  have to re-derive ADR-0006's project-split conventions from scratch.
- Days 22-25 ("API Gateway," "Identity service," "Event bus," "Base Security Framework") will need
  the same kind of modular-monolith reinterpretation this ADR gave Day 21, when they're started —
  flagged here so it isn't rediscovered from scratch each time. Likely shape: these become
  middleware/library concerns wired into the single `Api` host (extending
  `Platform.Security`/`Platform.Messaging`), not standalone services with their own containers —
  but that reinterpretation belongs to whichever day actually starts that work, not decided here.
- `docker-compose.yml` is unchanged by this ADR (already correct); this ADR records the decision
  to keep it as-is rather than introduce Kubernetes, so the choice has a citable rationale instead
  of just being "whatever was already there."

## Related

[[ADR-0003]] (Modular Monolith — the reason "base service template" needed reinterpreting),
[[ADR-0006]] (Clean Architecture adaptation — the `Context.csproj`/`Context.Infrastructure.csproj`
split this template packages), [[ADR-0008]] (containerization/CD — the `docker-compose.yml`/
`Dockerfile` this ADR confirms rather than replaces, and the Staging/Production deferral this ADR
leaves untouched), Day-Wise Plan Week 5 (Days 21-25, Core Infrastructure).
