# ADR-0006: Clean Architecture Adaptation — Bounded-Context-First, Domain+Application Merged

**Status:** Accepted
**Date:** 2026-07-27
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

The project structure established in ADR-0003 (bounded-context-first modules, each with a `Context.csproj` and `Context.Infrastructure.csproj`) and reinforced in ADR-0001 (EF Core isolated behind repository interfaces) informally follows Clean Architecture's Dependency Rule, but this had never been named or evaluated explicitly against the actual Clean Architecture model. The user asked directly whether the design follows Clean Architecture; this ADR records the honest answer — a deliberate adaptation, not the textbook version — so the distinction is documented rather than left ambiguous for a future reader (including future-us).

## Decision

The platform follows **Clean Architecture applied per bounded context**, with two specific, deliberate deviations from the textbook (Uncle Bob) model:

1. **Domain and Application are merged into one project per context** (`Context.csproj`), rather than kept as two separate projects/rings.
2. **The four rings are instantiated once per bounded context (17 times), not once for the whole solution** — i.e., Clean Architecture nested inside a bounded-context-first structure (ADR-0003), sometimes called "vertical-slice Clean Architecture" or "modular Clean Architecture."

## What Is Preserved (Matches Textbook Clean Architecture)

| Principle | How it's satisfied |
|---|---|
| Dependency Rule (dependencies point inward only) | `Context.Infrastructure.csproj` references `Context.csproj`, never the reverse |
| Dependency Inversion | Repository interfaces are defined in `Context.csproj` (Domain/Application); implementations live in `Context.Infrastructure.csproj` |
| Framework/persistence independence of business logic | ADR-0001: no EF Core dependency outside `*.Infrastructure` projects; ADR-0002: Rules Engine has zero persistence/framework coupling |
| Testability without DB/UI | Day 8 Coding Standards: Domain/Application tests use in-memory fakes, never a real database connection |
| Persistence as a swappable detail | This is *why* ADR-0001's PostgreSQL→SQL Server portability requirement is achievable at all |

## What Is Deliberately Different (Not Textbook Clean Architecture)

| Textbook Clean Architecture | This Project | Why |
|---|---|---|
| Entities and Use Cases are separate rings/projects | Merged into one `Context.csproj` per bounded context | Reduces project count (already ~90-100 projects total across 17 contexts, per the Day-Wise Plan's later scaffolding) and matches solo-developer velocity; the Dependency Rule direction is still preserved, just not enforced as a project-boundary between these two specific rings |
| One instance of the 4 rings for the whole application | 4 rings instantiated once per bounded context | Matches the bounded-context ownership model established Days 1-5 and ADR-0003; a single application-wide ring structure would have re-introduced the cross-context coupling risk those days were designed to avoid |
| Interface Adapters as their own ring/project, separate from Frameworks & Drivers | Single `Api` host project plays both roles | Acceptable simplification for MVP; a stricter separation can be introduced later without disturbing Domain/Application/Infrastructure if it proves necessary |

## Known Gap (Not Yet Closed)

Nothing currently enforces the Dependency Rule or the bounded-context isolation rule (Day 5: contexts communicate via events/read models, not direct references) at build time. A `Context.csproj` could add a direct `ProjectReference` to another context's project, or `Context.csproj` could accidentally reference `Context.Infrastructure.csproj`, and nothing would fail until manually caught in review.

**Follow-up action (not yet done, tracked here):** introduce architecture tests (e.g. `NetArchTest` or `ArchUnitNET`) once at least two or three bounded-context projects exist to test the rule against meaningfully — flagged for Phase 0/Phase 3 scaffolding, not Phase -1, since a single existing project (`Platform.Common`) cannot yet violate a cross-context boundary.

## Consequences

- Anyone (including future-us) reading the `src/` structure should expect Clean Architecture's *dependency direction* to hold, but should not expect to find separate `Entities`/`UseCases` projects per context — both live in one `Context.csproj`, organized as folders/namespaces within it.
- If a future need arises to split Domain from Application more strictly (e.g. if a context's use-case logic grows large enough that mixing it with entity definitions becomes unwieldy), that context can be split into `Context.Domain.csproj` + `Context.Application.csproj` independently of other contexts — this is a per-context decision, not a solution-wide one, consistent with the bounded-context-first philosophy.
- Until architecture tests are added, this ADR itself is the enforcement mechanism — code review must check new `ProjectReference` entries against both this ADR and ADR-0003/Day 5's event-vs-direct-call rules.

## Related

[[ADR-0001]] (EF Core isolation — the Infrastructure/Domain split this ADR builds on). [[ADR-0002]] (Rules Engine — framework-independent by construction). [[ADR-0003]] (bounded-context-first structure — the outer organizing principle this ADR nests Clean Architecture inside). Day 5 Context Map (events-vs-direct-call rules that the "known gap" above puts at risk without enforcement). Day 8 Coding Standards v1.0 (testability practices that depend on this layering).
