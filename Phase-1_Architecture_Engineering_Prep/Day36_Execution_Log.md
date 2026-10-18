# Day 36 Execution Log — Configuration Service

Phase 0, Week 8 (Days 36-40, Configuration & Notification Base), Day 36. Deliverable: "Config
service live, one value changed without redeploy."

---

## Step 1 — Settled the design before writing code

Plan mode: four questions put to the user, all answered with the recommended option — placement
(`Platform.Configuration` library in the `Api` host), storage (PostgreSQL typed key/value), live
propagation (30s in-memory cache + invalidate-on-write), write path (Admin-gated `/api/config/*`
CRUD returning old/new value). Recorded in `adr/0030-configuration-service.md`.

## Step 2 — Built `Platform.Configuration` and the admin API

`src/Platform/Configuration/`: `ConfigEntry`, `ConfigValueType`, `ConfigDbContext` (unique index on
`Key`), `ConfigValueValidator`, `IConfigService`/`ConfigService` (typed getters + `IsEnabledAsync`
+ cache), `ConfigCacheOptions`, `AddCustomsClearanceConfiguration`. `src/Api/ConfigAdmin/`:
`ConfigAdminEndpoints` + models. Wired into `Program.cs` (registration, startup table creation with
retry, route mapping — all behind `ConnectionStrings:ConfigDb` presence), `Api.csproj`,
`Dockerfile`, `docker-compose.yml`, `appsettings.Development.json`, `CustomsClearance.sln`, and
`TestApiFactory` (single-space placeholder for `ConnectionStrings__ConfigDb`).

## Step 3 — Unit tests and static checks

New `Platform.Configuration.Tests`: 24 tests, 97.36% line / 100% branch (80% gate). Full-solution
`dotnet build` (0 errors; only the documented NU1902), `dotnet format --verify-no-changes` exit 0,
`dotnet test` exit 0 — all 10 test projects green including the integration tests (the
`ConfigDb` placeholder keeps them infrastructure-free).

## Step 4 — Environment obstacle: host port 5432 already taken

First `docker compose up` failed: `Bind for 0.0.0.0:5432 failed: port is already allocated`. Cause:
a container from an unrelated project (`saferte_postgres`) holds 5432. It was **not** stopped.
Instead used a temporary compose override kept outside the repo (scratchpad) remapping only the
host side of the postgres port to 55432 (the services reach Postgres over the compose network, so
nothing else changes). The repo's `docker-compose.yml` still says `5432:5432`; anyone with a free
5432 is unaffected.

## Step 5 — Live verification through the Gateway

- Api log shows real `CREATE TABLE "ConfigEntries"` + `CREATE UNIQUE INDEX "IX_ConfigEntries_Key"`;
  `psql \d` confirms them.
- No token 401 / broker 403 / admin 201.
- Created `deminimis.gift.threshold-inr` = `5000` (Decimal) and flag `feature.new-duty-calc`.
  `/effective` → 5000; `PUT` → `{"oldValue":"5000","newValue":"10000"}`; `/effective` → 10000
  immediately. `psql` row confirms 10000 with `UpdatedAt` set. `api` container never restarted
  (uptime unbroken). Log line: `Config 'deminimis.gift.threshold-inr' changed from '5000' to '10000'`.
- 422s with BR-CONFIG-002/003/004 codes for bad value, non-Boolean flag, duplicate key; 404 on PUT
  of a missing key; `DELETE` 204 then `/effective` → `found:false`.

## Step 6 — CI smoke script

`build/scripts/smoke-test.sh` (with the same temporary port override via `COMPOSE_FILE`), unpiped:
exit 0 — Postgres and RabbitMQ round trips unaffected. Stack torn down by the script afterwards.

## Not done / deferred

- Audit wiring of config changes (Day 38). Event-based cache invalidation across instances
  (when scaling out). Seeding real initial config values (created via the API as needed).
- Nothing committed or pushed (awaiting explicit instruction).
