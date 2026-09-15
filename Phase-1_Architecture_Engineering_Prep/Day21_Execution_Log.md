# Day 21 Execution Log — Environment & Containerization

Phase 0, Week 5 (Core Infrastructure), Day 21 of the Day-Wise Plan. First day of Phase 0, following
Day 20's Phase -1 Exit Review sign-off.

---

## Step 1 — Pin down two decisions the Day-Wise Plan's wording left ambiguous for this project

The plan's Day 21 row says: "Set up local/dev Kubernetes (or Docker Compose for solo-scale dev) and
base service template project" → deliverable "Running hello world service in a container."

Two things needed a decision before proceeding, rather than assuming:

1. **Docker Compose vs. local Kubernetes.** Asked the user directly. Confirmed: **Docker Compose**
   — matches the plan's own "solo-scale dev" framing, and nothing exists yet (all 17 bounded
   contexts are still empty) that would justify Kubernetes' orchestration overhead.
2. **Whether to also decide the cloud/hosting target** (ADR-0008 explicitly deferred this exact
   decision to Day 21). Asked the user directly. Confirmed: **stay scoped to local dev only** —
   ADR-0008's Staging/Production rows remain "Not yet implemented, awaits a real hosting target,"
   deferred again rather than forced now with nothing yet to deploy.

## Step 2 — Recognize the "base service template" doesn't map onto this project literally

The Day-Wise Plan's Days 22-25 (API Gateway, Identity service, Event Bus, Base Security Framework)
read as written for a microservices topology. This project committed to a **Modular Monolith**
(ADR-0003) — one `Api` host, not one container per bounded context. A literal "new microservice
scaffold" template would contradict that decision.

Reinterpreted "base service template" as a **bounded-context project template**: the
`Context.csproj` + `Context.Infrastructure.csproj` split ADR-0006 already committed to, packaged so
the first real bounded-context build (Phase 3+) doesn't have to re-derive those conventions from
scratch. Documented this adaptation — and the two decisions from Step 1 — as `adr/0015-local-dev-
environment-and-context-template.md`, following the same "adapt and document explicitly" pattern
ADR-0006 itself set.

## Step 3 — Prove the "hello world in a container" deliverable end-to-end (not assumed from prior days)

Day 13 (ADR-0008) and Day 18 already containerized the `Api` host and proved it via CI's ephemeral
smoke test. Day 21 re-proves the same thing as an actual **local dev loop**, run directly today
rather than inferred from CI history:

1. Docker Desktop was not running on the workstation — started it, polled `docker info` until the
   engine responded (5s).
2. `docker compose up -d --build` — fresh image build (not reused from a prior day), `postgres`
   healthcheck (`pg_isready`) gated `api`'s startup per Day 18's fix; both containers reported
   `Healthy`/`Started`.
3. `GET http://localhost:8080/health` → `200 {"status":"Healthy","service":"CustomsClearance.Api"}`
   — the literal "hello world" deliverable.
4. `POST http://localhost:8080/diagnostics/widgets?name=day21-check` → `200`, with container logs
   confirming a real `CREATE TABLE "Widgets"` DDL statement executed against the `postgres`
   service (not an in-memory/mocked fallback) — proving the local dev loop is a genuine round trip
   against real Postgres, not just a container that starts.
5. `docker compose down` — containers/network removed; the named volume (`postgres-dev-data`) was
   left intact, consistent with a *dev* environment (data persists across `up`/`down` cycles;
   only a CI run tears the volume down too, per ADR-0008's "ephemeral, CI-only" framing for the
   Dev promotion row).

## Step 4 — Build the bounded-context template

Added `templates/BoundedContextTemplate/`:
- `Context.csproj.template` — Domain+Application, no EF Core reference (ADR-0006), references
  `Platform.Persistence`/`Platform.Messaging`.
- `Context.Infrastructure.csproj.template` — EF Core + Npgsql (versions pinned to match
  `Platform.Persistence`'s existing `8.0.10`), references the sibling `Context.csproj`.
- `README.md` — instantiation steps (copy, rename placeholders, add to `.sln`, wire into `Api`
  host, run Gate 1) and an explicit note on what's deliberately not templated (test projects,
  Dockerfile — Modular Monolith means no per-context container).

Not added to `CustomsClearance.sln` — these are `.template` files, not buildable projects, by
design (nothing should accidentally compile them before a real context claims them).

## Outcome

**Deliverable met**: local Docker Compose dev environment re-proven end-to-end against a real
`GET /health` + real Postgres-backed `POST /diagnostics/widgets` round trip. **Decision recorded**:
`adr/0015-local-dev-environment-and-context-template.md` (Accepted) — Docker Compose over local
Kubernetes, cloud/hosting target decision deferred again, "base service template" reinterpreted as
a bounded-context project template for the Modular Monolith. **New artifact**:
`templates/BoundedContextTemplate/` for Phase 3+ use.

**Status**: Complete. Next unstarted item is **Day 22 — API Gateway**, pending explicit instruction
to begin. Per ADR-0015's Consequences, Day 22 (and 23-25) will likely need the same kind of
modular-monolith reinterpretation Day 21 needed — flagged now so it isn't rediscovered from
scratch, but not decided ahead of that day's own work.
