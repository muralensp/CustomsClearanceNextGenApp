# ADR-0030: Configuration Service (Day 36)

**Status:** Accepted
**Date:** 2026-10-18
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 8 (Days 36-40, Configuration & Notification Base), Day 36. Task: "Build central
configuration service (feature flags, business parameters like currency, thresholds) - no
redeploy needed for changes." Deliverable: "Config service live, one value changed without
redeploy." Day 38 (Audit Trail End-to-End) is explicitly planned to use a config change as its
sample audited flow, so writes must expose old and new values.

This is deliberately distinct from the Rules Engine (ADR-0002/ADR-0013): rules are effective-dated,
condition-based clearance decisions; configuration is a flat set of named values and on/off flags
that code reads directly (e.g. a De Minimis gift threshold used by other code, a feature flag).

## Decision

Four design questions were settled with the user before any code was written:

1. **Placement - `Platform.Configuration` library inside the single `Api` host**, mirroring
   `Platform.RulesEngine`. Fits the Modular Monolith (ADR-0003); no new container. A separately
   deployed config service was rejected as heavier than ADR-0003/ADR-0015 call for.
2. **Storage - PostgreSQL typed key/value table.** `ConfigEntry` (unique `Key`, text `Value`,
   `ValueType` enum String/Integer/Decimal/Boolean stored as text, `Category`, `IsFeatureFlag`,
   `Description`) on `AuditableEntity<Guid>`, via `ConfigDbContext`. Same
   `EnsureCreatedAsync`-then-`CreateTablesAsync`-fallback startup pattern as Rules/Identity;
   registered only when `ConnectionStrings:ConfigDb` is present, with the admin route mapping
   behind the same guard (Minimal API map-time bindability lesson). No provider-specific SQL.
   Effective-dated/versioned values were rejected as scope beyond a "minimal" Day 36; a
   file/`IOptionsMonitor` approach was rejected because it gives admins no API and Day 38 nothing
   to audit.
3. **Propagation - short in-memory cache with TTL (default 30s) plus invalidate-on-write.**
   `IConfigService` reads through `IMemoryCache`; admin create/update/delete call
   `IConfigService.Invalidate(key)`, so the change is visible on this host immediately and via the
   TTL otherwise. Missing keys are negatively cached too (a missing flag reads as off) and
   cleared by the create call. Event-based invalidation over RabbitMQ was rejected as overkill for
   one host; it is the natural upgrade once a second Api instance exists.
4. **Write path - Admin-gated `/api/config/*` CRUD** (`PlatformPolicies.RequireAdmin`, same as
   `/api/rules`): `POST`, `GET` list, `GET /{key}`, `GET /{key}/effective` (reads through
   `IConfigService`, i.e. the cached path), `PUT /{key}`, `DELETE /{key}`. Responses are DTO
   projections. `PUT` returns `{key, oldValue, newValue, entry}` and logs the same - the data
   Day 38 will wire into the audit trail.

Validation (as `DomainValidationException`, mapped to 422): key required and at most 200 chars
(`BR-CONFIG-001`); `Value` must parse as the declared `ValueType`, invariant culture
(`BR-CONFIG-002`, in the pure `ConfigValueValidator`); a feature flag must be `Boolean`
(`BR-CONFIG-003`); duplicate `Key` (`BR-CONFIG-004`). `ValueType` and `Key` are immutable after
creation - `PUT` only changes `Value`/`Description`, so a type can never drift from its stored
text.

## Verification

Live against the real docker-compose stack, through the Gateway, unpiped exit codes:

- **Schema**: the Api's own log shows the real `CREATE TABLE "ConfigEntries"` and `CREATE UNIQUE
  INDEX "IX_ConfigEntries_Key"`; `psql \d "ConfigEntries"` confirms both as real constraints.
- **Auth matrix**: no token 401, broker token 403, admin token 200/201.
- **The deliverable**: created `deminimis.gift.threshold-inr = 5000` (Decimal) and a
  `feature.new-duty-calc` flag; read the first via `/effective` (the cached path), then `PUT` it to
  `10000`; the very next `/effective` returned `10000`, and `psql` showed the persisted row. The
  `api` container's uptime showed it was never restarted. The Api log carried `Config
  '...' changed from '5000' to '10000'`.
- **Negative paths**: duplicate key, non-parsing value (create and update), non-Boolean flag all
  returned 422 with the BR-CONFIG codes; `PUT` on a missing key 404; `DELETE` 204 followed by
  `/effective` reporting `found: false` (cache invalidated on delete).
- **Unit tests**: new `Platform.Configuration.Tests` (24 tests incl. cache hit, invalidation,
  negative caching and TTL expiry) - 97.36% line / 100% branch against the 80% CRUD-tier gate
  (ADR-0007).
- **Regression**: full-solution `dotnet build` (clean apart from the documented NU1902 advisory),
  `dotnet format --verify-no-changes`, `dotnet test`, and the actual CI `build/scripts/smoke-test.sh`
  all exit 0.

## Consequences

- Other code can now read business parameters and flags through `IConfigService` instead of
  hardcoding them; nothing consumes it yet, deliberately - consumers arrive with the bounded
  contexts that need them.
- The TTL means a change made through one Api instance can take up to 30s to be seen by another
  instance. Acceptable with a single host; revisit (RabbitMQ invalidation event) when scaling out.
- Config writes are logged but **not yet audited** via `Platform.Audit`; wiring them into the audit
  trail with old/new value, user and timestamp is Day 38's scope.
- Seeding of initial config values (currency, thresholds) is not done here; Day 39's reference
  data covers lookups, and real parameters will be created through this API.
- `ConfigDb` shares the single physical `customsclearance_dev` database like every other context
  (a deliberate placeholder, same as Diagnostics/Identity/Rules).

## Related

[[ADR-0003]] (Modular Monolith placement), [[ADR-0007]] (80% CRUD-tier coverage gate), [[ADR-0012]]
(repository/Unit-of-Work and `PlatformDbContext`), [[ADR-0025]] (the EnsureCreated/CreateTables
storage pattern reused), [[ADR-0028]] (the Admin-gated CRUD shape reused), [[ADR-0013]]/[[ADR-0002]]
(rules vs. configuration boundary).
