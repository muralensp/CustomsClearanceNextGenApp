# ADR-0032: Audit Trail End-to-End (Day 38)

**Status:** Accepted
**Date:** 2026-10-18
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 8 (Days 36-40), Day 38. Task: "Wire audit-lib into a real sample flow (e.g. config
change) to confirm full audit capture per PRD requirements." Deliverable: "Audit record visible
with old/new value, user, timestamp." Depends on Day 36 (the config service is the sample flow).

`Platform.Audit` (Day 17, ADR-0011) defined the shared `AuditEvent` model and an `IAuditWriter`
port, but its only implementation, `ConsoleAuditWriter`, writes a log line: nothing durable and
nothing queryable, and the log line does not even include the field-level old/new values. ADR-0004
requires an append-only PostgreSQL audit store; ADR-0011 left the DI seam for it.

## Decision

Four design questions were settled with the user before any code was written:

1. **Sink - new `Platform.Audit.Persistence` project.** `Platform.Audit` stays storage-agnostic and
   free of EF Core (ADR-0004/0006). The new project holds `AuditDbContext`, `AuditEventRecord`,
   `EfAuditWriter`, `AuditQueryService`, and `AddCustomsClearanceAuditPersistence(configureProvider)`,
   which *replaces* the `ConsoleAuditWriter` registration. The caller passes the `UseNpgsql(...)`
   action, so the library never references a database provider (ADR-0001). The Api host switches to
   the durable sink when `ConnectionStrings:AuditDb` is present; without it (integration tests) audit
   events still go to the console, keeping tests infrastructure-free (ADR-0009). Every existing and
   future `IAuditWriter` caller (including `/diagnostics/secure`) lands in the durable store with no
   code change.
2. **Append-only - application-level now, database-level at Day 41.** `AuditDbContext` throws on any
   attempt to update or delete a stored row (both `SaveChanges` overloads), exposes no mutation API,
   and is a plain `DbContext` (not `PlatformDbContext`, whose `UpdatedAt` stamping has no place on an
   immutable table). Database-level enforcement (revoked grants or triggers) is provider-specific and
   conflicts with ADR-0001's no-stored-procs/portability rule, so it is deferred to Day 41 Security
   Hardening. **This is a known, accepted gap**: anyone with direct database access can still alter
   rows (shown live: `UPDATE "AuditEvents"` succeeds at the database).
3. **Atomicity - sequential: save the change, then write the audit event; an audit failure surfaces as
   a 500 and is logged.** `EfAuditWriter` is a singleton using a short-lived `DbContext` from a factory,
   so an audit write is on its own connection and not part of the caller's transaction. **Known
   gap**: a process crash between the two commits could leave a config change with no audit row (and an
   audit failure returns 500 to a caller whose change is already committed). The shared-transaction or
   outbox approach that closes it is deferred; it would need a scoped, connection-sharing writer.
4. **Read access - Compliance Officer or Admin** (`RequireComplianceOfficerOrAdmin`):
   `GET /api/audit` (filters `entityType`, `entityId`, `performedBy`, `action`, `from`, `to`; `page`,
   `pageSize` clamped to 1-200; newest first) and `GET /api/audit/{auditEventId}`. Read-only by
   design.

**The sample flow:** every config create/update/delete (Day 36) now writes an `AuditEvent`:
`TechnicalAudit` (the category's own documentation lists "config change"), classification
`Internal`, `EntityType = ConfigEntry`, `EntityId` = the entry's id, `PerformedBy` = the JWT subject
(`sub`, mapped to `NameIdentifier`), and `FieldChanges` carrying the true old and new values
(create: Key/Value/ValueType/IsFeatureFlag from null; update: Value and/or Description; delete:
Key/Value to null). A PUT that changes nothing, or that is rejected (422/404), is not audited.
`FieldChanges` are stored as a JSON text column (read back together with the event, never queried
alone; portable).

## Verification

Live through the Gateway against real PostgreSQL, using the repo's unmodified `docker-compose.yml`
(host port 5432 had been freed), unpiped exit codes:

- **Schema**: `psql \d "AuditEvents"` shows the table with indexes on (EntityType, EntityId),
  PerformedBy, OccurredAt, CorrelationId.
- **The deliverable**: as admin, created `deminimis.gift.threshold-inr = 5000`, changed it to `10000`,
  then deleted it. Read back as a *compliance officer*, `GET /api/audit?entityId=...` returned three
  events, newest first, each with timestamp and acting user: `ConfigCreated`, `ConfigUpdated`
  (`Value '5000' -> '10000'`), `ConfigDeleted`. `PerformedBy` equals the `admin` row's `Id` in the
  `Users` table.
- **Negative cases**: a no-op PUT (same value) and an invalid PUT (422) produced no audit rows (3
  events for 5 requests).
- **Access**: `/api/audit` returned 200 for admin and compliance, 403 for supervisor and broker, 401
  without a token.
- **Correlation**: the `X-Correlation-Id` on a real PUT response equals the `CorrelationId` stored on
  its audit row, tying HTTP request, logs and audit trail together.
- **Tests**: new `Platform.Audit.Persistence.Tests` (13 tests: round-trip of every field incl. old/new
  values, modify and delete both refused with the row unchanged, sync and async guards, every query
  filter, paging/clamping, DI replacement of the console writer) - 100% line/branch against the 95%
  gate (ADR-0007: compliance-critical logic, core tier). The config endpoints' audit calls are
  covered by the live run and the extended smoke script (the Api host has no unit-test tier).
- **Regression**: full-solution build (only the documented NU1902; one new Sonar S6966 in a test was
  fixed, not suppressed), `dotnet format --verify-no-changes`, `dotnet test` (12 projects) all exit 0.
  `build/scripts/smoke-test.sh` extended with a config-change -> `/api/audit` old/new-value check;
  exit 0 from a fresh-volume cycle.

## Consequences

- The audit trail is now durable, attributable, field-level and queryable; any module that injects
  `IAuditWriter` and calls `AuditEvent.Create()` gets the same treatment. Only config changes write
  audit events so far (plus the existing `/diagnostics/secure` sample). Rules admin (Day 34) and other
  mutations are not yet audited - a follow-up, tracked by Gate 2's "audit-worthy action" item.
- Stored values are the true values (masking is a display concern, per `FieldChange`'s contract), so
  secrets must never be kept in configuration values; reading the audit trail is itself unaudited.
- Retention (5 years minimum) and monthly partitioning from ADR-0004 are not implemented; neither is
  tamper-evidence (hash chaining). Both are open items.
- The three honest gaps - database-level immutability (Day 41), audit/change atomicity, and
  retention/partitioning - are recorded here rather than left implicit.
- Side-finding, not changed: the Gateway response carries `X-Correlation-Id` twice with the same value
  (header added at both hops). Harmless; worth tidying separately.

## Related

[[ADR-0004]] (the audit standard implemented here), [[ADR-0011]] (the model and the seam),
[[ADR-0006]] (storage-agnostic Platform library + Infrastructure adapter), [[ADR-0001]] (provider
portability), [[ADR-0007]] (coverage tiers), [[ADR-0009]] (infrastructure-free integration tests),
[[ADR-0030]] (the config service audited here), [[ADR-0017]] (the JWT whose subject is `PerformedBy`).
