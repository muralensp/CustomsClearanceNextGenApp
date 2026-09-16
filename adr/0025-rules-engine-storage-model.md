# ADR-0025: Rules Engine - Storage Model (Day 31)

**Status:** Accepted
**Date:** 2026-09-16
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 7 (Days 31-35, Rules Engine Skeleton) begins - the Day-Wise Plan marks this whole
week non-parallelizable ("schema, evaluation pipeline, versioning, and admin API form a strict
build chain"), a different working shape from Week 6's parallel-then-merge. Day 31's task: "Implement
DB schema for rules (from Phase -1 design spike): rule, condition, priority, action, version."
Deliverable: "Rules storage schema migrated."

ADR-0002 (Day 8, config-driven rules engine) and ADR-0013 (Day 19 design spike) already settled
the shape this schema must take: `Rule` (identity, category, priority) -> `RuleVersion`
(effective-dated) -> `RuleCondition` (AND-only per version), evaluated against an assembled
`Facts` bag, per Day 3 ER Diagram v0.2 §5. Day 31 turns that design into real EF Core entities and
proves them against PostgreSQL - the evaluation pipeline itself (`IRuleSetLoader`,
`IConditionEvaluator`, etc.) is Day 32's scope, not this one's.

## Decision

1. **New `Platform.RulesEngine` library** (`src/Platform/RulesEngine/`), matching ADR-0013's own
   naming ("CustomsClearance.Platform.RulesEngine"), referencing only `Platform.Persistence` (for
   `AuditableEntity<TId>`/`PlatformDbContext`/`EfRepository<TEntity,TId>`) - no framework
   dependency beyond EF Core, keeping the eventual evaluation pipeline testable without a database
   (ADR-0013's own stated constraint).
2. **Entities**, all `AuditableEntity<Guid>`:
   - `Rule` - `RuleCode` (unique business identifier), `Name`, `Description`, `Category`
     (`RuleCategory` enum, the same 9 values the Day 19 spike validated), `Priority` (int, PRD
     Ch.5.9: 1 = Legal Restrictions highest), `Active` (soft-deactivation only - a `Rule` is never
     hard-deleted, since past `DecisionTrace` rows will reference `RuleVersion`s authored under
     it).
   - `RuleVersion` - `RuleId` (FK), `VersionNumber`, `EffectiveFrom`/`EffectiveTo` (nullable,
     open-ended), and the **action** the Day-Wise Plan's wording names: `OutcomeFactName` +
     `OutcomeValue` - the Facts key/value this version sets when it wins its pipeline stage
     (ADR-0013's `ApplyOutcome`). Deliberately generalized beyond the Day 19 spike's
     clearance-type-only `SetsClearanceType` field, since PRD Ch.5.7's later stages (Permit,
     Warning) need to react to flags a rule sets (e.g. `ControlledGoodsFlag`), not only a clearance
     type.
   - `RuleCondition` - `RuleVersionId` (FK), `FactName` (open string, per ADR-0013's Facts design),
     `Operator` (`ConditionOperator` enum: Equals/NotEquals/GreaterThan/LessThan/In/Between, the
     full set ADR-0013 named - the Day 19 spike only implemented the first four), `Value` (text,
     typed parsing deferred to Day 32's evaluator per-operator).
3. **`RulesDbContext`** - same real-PostgreSQL-via-`EnsureCreatedAsync`-then-`CreateTablesAsync`-
   fallback pattern as `DiagnosticsDbContext`/`IdentityDbContext` (no EF Core Migrations pipeline
   exists anywhere in this codebase, a deliberate, already-named gap). Fluent config: `RuleCode`
   unique index; `(RuleId, VersionNumber)` composite unique index (the one sub-case of ADR-0013's
   "no two versions may overlap" invariant a database constraint can cheaply enforce - the
   effective-date-window non-overlap check itself stays an application-level concern, per
   ADR-0013's own scoping); cascade deletes `Rule -> RuleVersion -> RuleCondition`; enums stored as
   readable strings (`HasConversion<string>()`), not integers.
4. **Wired into the `Api` host** the same way `DiagnosticsDb`/`IdentityDb` are:
   `ConnectionStrings:RulesDb` presence decides registration ("absence means skip",
   ADR-0009-compliant); `TestApiFactory` blanks it for integration tests.

## Two real bugs found and fixed during live verification

1. **Wrong DbContext resolved, silently.** `RuleRepository`/`RuleVersionRepository`/
   `RuleConditionRepository` were first written accepting the abstract `PlatformDbContext` (the
   type `EfRepository<TEntity,TId>`'s own constructor is typed to). The Api host registers exactly
   one `PlatformDbContext` DI mapping (`AddScoped<PlatformDbContext>(sp =>
   sp.GetRequiredService<DiagnosticsDbContext>())`, Day 18) - so these repositories silently
   resolved `DiagnosticsDbContext`, not `RulesDbContext`, and the first live insert threw "Cannot
   create a DbSet for 'Rule' because this type is not included in the model for the context."
   Fixed by having each repository's constructor accept the concrete `RulesDbContext` and pass it
   to `EfRepository`'s base constructor (accepted there as a `PlatformDbContext`, since
   `RulesDbContext` is one) - this sidesteps the single-registration ambiguity entirely, since the
   base repository then holds a reference to the actual `RulesDbContext` instance, model and all.
2. **JSON serialization cycle.** The diagnostic GET endpoint's first version returned the raw
   `Rule` entity graph directly; `Rule.Versions` <-> `RuleVersion.Rule` (and
   `RuleVersion.Conditions` <-> `RuleCondition.RuleVersion`) are bidirectional navigation
   properties, which `System.Text.Json`'s default serializer cannot walk - it threw "A possible
   object cycle was detected" at the first real GET. Fixed by projecting to an anonymous DTO
   shape instead of serializing entities directly - flagged in the endpoint's own comment as a
   discipline the real Day 34 admin API will also need (never return raw EF entities with
   bidirectional navigations over the wire).

## Verification

Full `docker compose down -v` / `up -d --build`, then real end-to-end proof, not just a clean
build:
- `docker logs api` confirmed all **five real `CREATE TABLE` DDL statements** (`Widgets`, `Users`,
  `Rules`, `RuleVersions`, `RuleConditions`), including the FK constraints
  (`FK_RuleVersions_Rules_RuleId ... ON DELETE CASCADE`,
  `FK_RuleConditions_RuleVersions_RuleVersionId ... ON DELETE CASCADE`).
- `POST /diagnostics/rules` (after both fixes above) - real 200, a Rule + RuleVersion + RuleCondition
  actually persisted.
- `GET /diagnostics/rules/{id}` - real 200, the full graph read back correctly: `Category`/
  `Operator` as readable strings, `EffectiveFrom`/`EffectiveTo` as dates, the condition's
  `FactName`/`Operator`/`Value` intact.
- **Direct `psql \d` inspection** (not just EF Core's own view of the schema) confirmed the
  `IX_Rules_RuleCode` and `IX_RuleVersions_RuleId_VersionNumber` unique indexes and both cascade
  FKs exist as real Postgres constraints, not just C# annotations.
- `Platform.RulesEngine.Tests` (EF Core InMemory provider, per ADR-0009's "unit tests must not
  require infrastructure") - full graph round trip, cascade-delete behavior, and both new
  repositories, at 97.14% line / 100% branch coverage against the ADR-0007 95% core-tier gate
  (Rules Engine is core business/rules logic, not CRUD-tier).
- Full-solution `dotnet build`/`dotnet format --verify-no-changes`/`dotnet test`, all clean with
  unpiped exit codes (0/0/0). CI's `build/scripts/smoke-test.sh` re-run clean, Postgres/RabbitMQ
  round-trip checks (Days 18/24) unaffected. No RabbitMQ startup-race recurrence this day.

## Consequences

- Day 32 (Rule Evaluation Pipeline) builds `IRuleSetLoader`'s real EF Core implementation directly
  against this schema - the effective-dating query shape ADR-0013 already specified
  (`EffectiveFrom <= evaluationDate AND (EffectiveTo IS NULL OR EffectiveTo >= evaluationDate)`)
  needs no schema change to support.
- The `RuleRepository` family's fix (concrete `RulesDbContext`, not abstract `PlatformDbContext`)
  is a pattern every future bounded context's own repositories must follow once more than one
  `PlatformDbContext`-derived context exists in the same process - flagged here as a reusable
  lesson, since it will recur the moment a second real bounded context (not a Diagnostics/Identity/
  RulesEngine placeholder) is added.
- No EF Core Migrations pipeline yet (same named gap as Diagnostics/Identity) - schema changes
  during Days 32-35 continue to rely on `EnsureCreatedAsync`/`CreateTablesAsync` against a
  dev-only, disposable Postgres volume. A real Migrations pipeline is a Phase 3+ concern once real
  bounded contexts with production data exist.
- `OutcomeFactName`/`OutcomeValue` generalizing beyond the Day 19 spike's `SetsClearanceType` is a
  deliberate widening of scope within what ADR-0013 already implied (`ApplyOutcome` mutating
  arbitrary Facts, not just a clearance type) - not a new architectural decision.

## Related

[[ADR-0002]] (the config-driven decision this schema implements), [[ADR-0013]] (Day 19 design
spike - the five-interface pipeline this schema is built for), [[ADR-0012]] (Persistence platform
library - `AuditableEntity`/`PlatformDbContext`/`EfRepository` this schema is built on), Day 3 ER
Diagram v0.2 §5 (Rule/RuleVersion/RuleCondition/DecisionTrace entities), Day-Wise Plan Days 31-35
(Rules Engine Skeleton, non-parallelizable build chain).
