# Day 38 Execution Log — Audit Trail End-to-End

Phase 0, Week 8 (Days 36-40), Day 38. Deliverable: "Audit record visible with old/new value, user,
timestamp."

---

## Step 1 — Read what existed, found the real gap, settled the design

`Platform.Audit` (Day 17) has the `AuditEvent` model and an `IAuditWriter` port, but only
`ConsoleAuditWriter` implements it — a log line that doesn't even include the field-level old/new
values. A durable, queryable store was the missing piece (ADR-0004 §9 / ADR-0011's seam). Four
questions put to the user, all answered with the recommended option: sink in a new
`Platform.Audit.Persistence` project; append-only enforced at application level now and DB level at
Day 41; sequential change-then-audit (audit failure → 500 + logged); read access for Compliance
Officer or Admin. Recorded in `adr/0032-audit-trail-end-to-end.md`.

## Step 2 — Built the store, wired the flow, added the read API

`src/Platform/AuditPersistence/`: `AuditEventRecord`, `AuditDbContext` (rejects update/delete),
`EfAuditWriter` (singleton over `IDbContextFactory`), `AuditQueryService`,
`AddCustomsClearanceAuditPersistence(configureProvider)` (replaces the console writer).
`src/Api/ConfigAdmin/ConfigAudit.cs` + edits to `ConfigAdminEndpoints.cs` (create/update/delete each
write a `TechnicalAudit` event with field changes; no-op PUTs not audited).
`src/Api/AuditTrail/AuditEndpoints.cs` (`GET /api/audit`, `GET /api/audit/{id}`). Wired in
`Program.cs` (registration + table creation with retry + route mapping, all behind
`ConnectionStrings:AuditDb`), `Api.csproj`, `Dockerfile`, `docker-compose.yml`,
`appsettings.Development.json`, `TestApiFactory`, `CustomsClearance.sln`.

## Step 3 — Tests and static checks

`Platform.Audit.Persistence.Tests`: 13 tests, 100% line/branch (95% gate). One new Sonar warning
(S6966 in a test) fixed rather than suppressed. Full-solution build (0 errors, only the documented
NU1902), `dotnet format --verify-no-changes` exit 0, `dotnet test` exit 0 (12 projects).

## Step 4 — Environment

Host port 5432 had been freed (the unrelated `saferte_postgres` container was stopped), so this
day ran on the repo's **unmodified** `docker-compose.yml` — no temporary override needed. No
RabbitMQ first-boot race this time.

## Step 5 — Live verification through the Gateway

- `psql \d "AuditEvents"`: table + 4 indexes exist.
- As admin: created `deminimis.gift.threshold-inr = 5000`, changed to `10000`, deleted. As
  *compliance officer*: `GET /api/audit?entityId=<id>` → 3 events newest-first (`ConfigDeleted`,
  `ConfigUpdated` with `Value '5000' -> '10000'`, `ConfigCreated`), each with timestamp, category
  `TechnicalAudit`, classification `Internal`, correlation id and `PerformedBy` =
  `9f186f1e-…`, which equals the `admin` row's `Id` in `Users`.
- A no-op PUT and an invalid (422) PUT produced no audit rows.
- `/api/audit`: admin 200, compliance 200, supervisor 403, broker 403, no token 401.
- Correlation: a real PUT's `X-Correlation-Id` response header equals the `CorrelationId` stored on
  its audit row.
- Honest check of the Day-41 gap: an `UPDATE "AuditEvents"` inside a rolled-back transaction
  **succeeded** at the database (`UPDATE 2`) — immutability is application-level only today.

## Step 6 — CI smoke script

Extended `build/scripts/smoke-test.sh`: log in as admin, create + change a config value, query
`/api/audit?entityId=…`, assert both the old and new value are present. Ran unpiped from a
fresh-volume cycle on the default compose file: exit 0 (Postgres, RabbitMQ, notification and audit
round trips all passed).

## Findings / side-notes

- The first correlation-id check returned nothing: the Gateway response carries `X-Correlation-Id`
  **twice** (same value), so my shell variable held two lines. Re-checked with the first value and it
  matched. The duplicate header is pre-existing and harmless; not changed (out of scope), noted in
  the ADR.
- A transient shell-heredoc parse problem recurred; worked around by running patch scripts from files.

## Not done / deferred

- DB-level immutability (Day 41); change/audit atomicity (shared transaction or outbox); retention
  (5 years) and monthly partitioning; tamper-evidence (hash chain); auditing reads of the audit trail;
  auditing other mutations (e.g. the Day 34 rules admin API).
- Nothing committed or pushed (awaiting explicit instruction).
