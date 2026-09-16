# Day 31 Execution Log — Rules Engine: Storage Model

Phase 0, Week 7 (Days 31-35, Rules Engine Skeleton), Day 31 - first day of a Day-Wise Plan week
explicitly marked non-parallelizable (strict build chain: schema → pipeline → versioning/trace →
admin API → review). Deliverable: "Rules storage schema migrated."

---

## Step 1 — Re-read the settled design before building

Re-read ADR-0002 (config-driven decision) and ADR-0013 (Day 19 design spike: five-interface
pipeline, Rule/RuleVersion/RuleCondition model) and the Day 19 spike code
(`spike/RulesEngineSpike/Engine.cs`) before writing any schema, per the standing discipline of
building against a settled design rather than re-deriving it.

## Step 2 — Build the schema as a new Platform.RulesEngine library

`src/Platform/RulesEngine/` (new project, referencing only `Platform.Persistence`):
- `Rule` (RuleCode, Name, Description, Category, Priority, Active) - `AuditableEntity<Guid>`.
- `RuleVersion` (RuleId FK, VersionNumber, EffectiveFrom/EffectiveTo, OutcomeFactName/OutcomeValue
  - generalized beyond the spike's clearance-type-only field, since later PRD stages react to
  arbitrary Facts flags a rule sets).
- `RuleCondition` (RuleVersionId FK, FactName, Operator, Value) - `ConditionOperator` enum
  extended to all 6 values ADR-0013 named (the spike only implemented 4).
- `RulesDbContext` (unique index on RuleCode; unique composite index on
  RuleId+VersionNumber; cascade deletes; enums stored as readable strings) - same
  EnsureCreatedAsync/CreateTablesAsync-fallback pattern as Diagnostics/Identity (no EF Migrations
  pipeline exists in this codebase).
- `RuleRepository`/`RuleVersionRepository`/`RuleConditionRepository` - thin EfRepository wrappers.

Added to `CustomsClearance.sln`; wired into `Api`'s `Program.cs` (ConnectionStrings:RulesDb
presence-decides-registration, same as DiagnosticsDb/IdentityDb), `appsettings.Development.json`,
`docker-compose.yml`, `Api`'s Dockerfile/csproj, and `TestApiFactory` (blanked for integration
tests, ADR-0009).

## Step 3 — Unit tests, coverage gate

New `Platform.RulesEngine.Tests` (EF Core InMemory provider, ADR-0009-compliant) - full graph
round trip (Rule → RuleVersion → RuleCondition with navigation properties), cascade-delete
behavior, both new repositories. Set the coverage threshold to **95%** (not Persistence's 80%),
since ADR-0002/ADR-0007 classify the Rules Engine as core business/rules logic, not a CRUD-tier
platform library. Achieved 97.14% line / 100% branch.

## Step 4 — Live verification against real PostgreSQL surfaced two real bugs

Fresh `docker compose down -v` / `up -d --build`, then exercised a dev-only diagnostic endpoint
(`POST`/`GET /diagnostics/rules`, same proof pattern as Diagnostics/Identity):

1. **`docker logs api`** confirmed 5 real `CREATE TABLE` statements, including both cascade FK
   constraints - the schema genuinely migrated, not just compiled.
2. **First `POST /diagnostics/rules`** returned 500: `"Cannot create a DbSet for 'Rule' because
   this type is not included in the model for the context."` Root cause: the repository
   constructors accepted the abstract `PlatformDbContext` type, and the Api host has exactly one
   `PlatformDbContext` DI registration (pointing at `DiagnosticsDbContext`, Day 18) - so the
   repositories silently resolved the wrong context. Fixed by having each repository accept the
   concrete `RulesDbContext` instead, passed through to `EfRepository`'s base constructor.
3. **First `GET /diagnostics/rules/{id}`** (after fix #2) returned 500: `"A possible object cycle
   was detected"` - `Rule.Versions` and `RuleVersion.Rule` are bidirectional navigation properties,
   which `System.Text.Json` cannot serialize directly. Fixed by projecting to an anonymous DTO
   instead of returning the raw entity graph.
4. **Re-tested after both fixes**: `POST` returned real 200s with real IDs; `GET` returned the
   full graph correctly (enum values as readable strings, dates intact, nested conditions present).
5. **Direct `psql \d "Rules"` / `\d "RuleVersions"`** (not just trusting EF Core's own model view)
   confirmed the unique indexes and cascade FKs exist as real Postgres constraints.

## Step 5 — Final clean-up verification

`dotnet test` (full solution, all 9 test projects) clean, unpiped exit 0. `dotnet format
--verify-no-changes` clean. `docker compose down -v`, then `build/scripts/smoke-test.sh` (the
actual CI script) - unpiped exit 0, Postgres/RabbitMQ round-trip checks (Days 18/24) unaffected.
No RabbitMQ startup-race recurrence this day.

## Outcome

**Deliverable met, with two real bugs found and fixed via live verification, not assumed from a
clean build**: the Rule/RuleVersion/RuleCondition schema is migrated against real PostgreSQL, with
real FK/unique constraints, proven via a genuine insert-then-read round trip - not just "the
migration script ran." **Decision recorded**: `adr/0025-rules-engine-storage-model.md` (Accepted).

**Status**: Complete. Per the Day-Wise Plan's strict build chain, **Day 32 — Rule Evaluation
Pipeline** (IRuleSetLoader's real implementation against this schema, IConditionEvaluator,
IPriorityResolver, orchestration) is next, pending explicit instruction to begin.
