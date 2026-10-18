# ADR-0033: Seed Data & Reference Tables (Day 39)

**Status:** Accepted
**Date:** 2026-10-18
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 8 (Days 36-40), Day 39. Task: "Load initial India reference data stubs: country codes,
currency (INR + majors), unit-of-measure list (placeholder ahead of full tariff sourcing in Phase
1)." Deliverable: "Reference/lookup tables seeded." The plan's reminder flag for this day is
explicit: only placeholder data is loaded; full India tariff master (ITC-HS) sourcing and licensing
is a Phase 1 workstream, and nothing here may be assumed production-ready. Independent of Days
36/37 (parallelizable in the plan).

## Decision

Four design questions were settled with the user before any code was written:

1. **Placement - `Platform.ReferenceData` library inside the `Api` host**, same shape as
   `Platform.Configuration`/`Notifications` (ADR-0003): `ReferenceDataDbContext` with `Country`,
   `Currency` and `UnitOfMeasure` tables, registered when `ConnectionStrings:ReferenceDataDb` is
   present. Storing them as Config entries was rejected: a flat key/value store is the wrong shape for
   multi-column lookup tables.
2. **Scope - deliberate stub sets**: 49 countries (India and its main trading partners), 13 currencies
   (INR + 12 majors), 45 India unit quantity codes (UQC). The full ISO 3166 country list (249) and
   full ISO 4217 are a Phase 1 sourcing task, not written from memory here.
3. **Seeding - idempotent insert-missing-by-`Code` from embedded JSON files** (`Data/*.json`), run at
   startup after table creation. Data lives in reviewable files, not C# literals. Because it only
   inserts codes that are absent, restarting is safe, codes added to the files propagate, and rows
   someone has edited or deactivated are never overwritten or reactivated.
4. **API - read-only**: `GET /api/reference/{countries|currencies|units}` (active rows by default,
   `?includeInactive=true` for all) and `GET .../{code}` (case-insensitive), for any authenticated
   user. No write endpoint: editing reference data belongs to Phase 1 master-data work once real
   sources are chosen. A POST returns 405.

**Provenance is part of the data.** Every row carries `Source` (`Placeholder-Day39`) and `IsVerified`
(`false`), included in every API response, and a unit test fails if any dataset is not marked that
way. Business keys: country `Code` = ISO 3166-1 alpha-2 (plus a unique `Alpha3Code`); currency `Code`
= ISO 4217 alphabetic (plus `NumericCode`, `MinorUnits`); unit `Code` = UQC. Country names are common
English names, not the official ISO short names.

## Data accuracy - read this before relying on the data

The three datasets were **written from the model's recall of the ISO 3166-1, ISO 4217 and ICEGATE UQC
standards; they were not copied from, or cross-checked against, an official source in this session.**
Structural validity is tested (code formats, uniqueness, INR = 356/2 decimals, JPY = 0 decimals, common
UQCs present), but whether each *name* and *code* matches the official master is **not verified** -
the UQC list in particular should be compared with the official ICEGATE UQC master. That is exactly why
every row is `IsVerified = false` and why verification against official sources is a Phase 1 task.

## Verification

Live through the Gateway against real PostgreSQL on the repo's unmodified `docker-compose.yml`, with
unpiped exit codes:

- **Seed**: Api log `Reference data seeded: 49 countries, 13 currencies, 45 units inserted
  (placeholder data)`; `psql` confirms 49/13/45 rows, all `Source = Placeholder-Day39`,
  `IsVerified = false`, with unique indexes on `Code` (and `Alpha3Code`) for each table.
- **API**: no token 401; broker token lists 49/13/45; `currencies/inr` returns INR/356/2 (lower-case
  lookup works); `countries/in`, `units/kgs` return the right rows; unknown code 404; POST 405.
- **Idempotency and edit-preservation**: set `NP` to inactive and renamed it directly in Postgres,
  restarted the Api: log `0 countries, 0 currencies, 0 units inserted`; active list 48, with
  `includeInactive=true` 49; `NP` kept its edited name and stayed inactive.
- **Tests**: new `Platform.ReferenceData.Tests` (11 tests: dataset sanity incl. uniqueness and
  provenance markers, seeding inserts/idempotency/no-overwrite/inserts-only-missing, unique-index
  model check) - 100% line against the 80% gate (ADR-0007, master-data tier).
- **Regression**: full-solution build (only the documented NU1902), `dotnet format
  --verify-no-changes`, `dotnet test` (13 projects) all exit 0. `build/scripts/smoke-test.sh` extended
  to read `countries/IN`, `currencies/INR`, `units/KGS` as admin; exit 0 from a fresh-volume cycle.

## Consequences

- Other modules (declarations, valuation, duty) now have lookup tables to reference by code; nothing
  references them yet, and there are no foreign keys from other contexts (each context keeps its own
  tables, ADR-0006), so consumers store the code and validate against the lookup API/context.
- `Source`/`IsVerified` give Phase 1 a clean migration path: replace placeholder rows with sourced ones
  and flip `IsVerified`, without changing consumers.
- Not covered: HS code / ITC-HS tariff data (Phase 1), ports/airports/UN-LOCODE, state codes, effective
  dating of reference rows, localisation of names, and admin editing/auditing of reference data (the
  Day 38 audit trail does not cover these tables because nothing can modify them through the API).
- Edits made directly in the database (as in the verification above) are preserved by the seeder and
  are not audited.

## Related

[[ADR-0003]] (placement), [[ADR-0006]] (no cross-context foreign keys), [[ADR-0007]] (80% master-data
gate), [[ADR-0009]] (infrastructure-free integration tests), [[ADR-0030]] (same wiring pattern),
[[ADR-0032]] (audit trail not yet applied here, since there are no write paths).
