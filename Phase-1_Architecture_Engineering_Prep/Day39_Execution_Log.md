# Day 39 Execution Log — Seed Data & Reference Tables

Phase 0, Week 8 (Days 36-40), Day 39. Deliverable: "Reference/lookup tables seeded."

---

## Step 1 — Read the plan's flag, then settled the design with the user

The plan's reminder for this day: only placeholder data; full ITC-HS tariff sourcing is Phase 1.
No reference-data code existed. Four questions put to the user, all answered with the recommended
option: `Platform.ReferenceData` in the Api host; stub-size datasets (49 countries, INR + 12 majors,
India UQC units) rather than the full ISO lists written from memory; idempotent insert-missing-by-code
seeding from embedded JSON files; read-only API for any authenticated user. Recorded in
`adr/0033-seed-data-and-reference-tables.md`.

## Step 2 — Built the library, seeder and read API

`src/Platform/ReferenceData/`: `ReferenceEntity` base (`Code`, `Name`, `IsActive`, `Source`,
`IsVerified`) + `Country`/`Currency`/`UnitOfMeasure`, `ReferenceDataDbContext` (unique `Code` index
per table, unique `Alpha3Code`), `ReferenceDataSeeder` (loads embedded `Data/countries.json`,
`currencies.json`, `units.json`; inserts only absent codes), the three JSON datasets.
`src/Api/ReferenceData/ReferenceDataEndpoints.cs`: `GET /api/reference/{countries|currencies|units}`
and `/{code}`. Wired into `Program.cs` (registration, table creation + seeding with the usual retry,
route mapping — all behind `ConnectionStrings:ReferenceDataDb`), `Api.csproj`, `Dockerfile`,
`docker-compose.yml`, `appsettings.Development.json`, `TestApiFactory`, `CustomsClearance.sln`.

## Step 3 — Tests and static checks

`Platform.ReferenceData.Tests`: 11 tests, 100% line (80% gate). They validate the data files
themselves (code formats, uniqueness, INR/JPY decimals, common UQCs present, every file marked
`Placeholder-Day39` / unverified) as well as the seeder. Full-solution build (0 errors, only the
documented NU1902), `dotnet format --verify-no-changes` exit 0, `dotnet test` exit 0 (13 projects).

## Step 4 — Live verification through the Gateway (default compose file)

- First `docker compose up` hit the known RabbitMQ first-boot race (`rabbitmq is unhealthy`,
  ADR-0028); a plain second `up` succeeded with no manual intervention.
- Api log: `Reference data seeded: 49 countries, 13 currencies, 45 units inserted`. `psql`: 49/13/45
  rows, all `Placeholder-Day39` / `IsVerified=false`; unique indexes present.
- API: no token 401; broker lists 49/13/45; `currencies/inr` → INR/356/2 (case-insensitive);
  `countries/in`, `units/kgs` correct; unknown code 404; POST 405.
- Idempotency: set `NP` inactive + renamed it directly in Postgres, restarted the Api → log `0/0/0
  inserted`; active list 48, `includeInactive=true` 49; `NP` kept its edit.

## Step 5 — CI smoke script

Extended `build/scripts/smoke-test.sh` (admin token already in hand from the Day 38 block) to read
`countries/IN`, `currencies/INR`, `units/KGS` and assert the expected names. Ran it unpiped from a
fresh-volume cycle: exit 0 — Postgres, RabbitMQ, notification, audit and reference-data checks all
passed.

## Honest note on data accuracy

The datasets were written from recall of ISO 3166-1 / ISO 4217 / the ICEGATE UQC list, **not
checked against an official source this session**. Tests prove structure, not correctness of each
name/code. Hence `IsVerified = false` on every row, surfaced in every API response; verifying against
the official masters (especially the UQC list) is a Phase 1 task.

## Not done / deferred

- Full ISO 3166/4217 lists, HS/ITC-HS tariff data, ports/UN-LOCODE, state codes, effective dating,
  admin editing + auditing of reference data, verification against official sources.
- Nothing committed or pushed (awaiting explicit instruction).
