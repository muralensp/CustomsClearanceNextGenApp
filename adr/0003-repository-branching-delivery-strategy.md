# ADR-0003: Repository, Branching & Delivery Strategy

**Status:** Accepted
**Date:** 2026-07-24
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Day 9 of the Day-Wise Plan calls for repo structure, branching model, commit conventions, and a PR-to-self discipline for traceability, given the project is solo-developed (per `project_setup` memory).

The user supplied `Repo_BranchingStrategy_NSPMV1.docx`, an external analysis recommending a Monorepo + Modular Monolith + Trunk-Based Development strategy (its own "ADR-0010"). This ADR evaluates that analysis, adopts what fits, and corrects what conflicts with decisions already locked in this project (ADR-0001, ADR-0002, Day 1-8 deliverables).

## Decision

Adopt: **Monorepo**, organized as a **Modular Monolith with bounded-context-first module boundaries** (not layer-first, correcting Day 8's initial structure), **Trunk-Based Development** with short-lived feature branches, **Conventional Commits**, and **mandatory pull requests** (including solo PR-to-self), per the analysis in Section "Assessment of Supplied Document" below.

## Assessment of Supplied Document

### Adopted as-is

- **Monorepo over multi-repo.** Correct and well-justified: this platform's 17 bounded contexts (Day 1/4) share a rules engine (ADR-0002), event contracts (Day 5), OpenAPI specs (Day 7), common libraries (Phase 0 Days 16-18), and a single solo developer — multi-repo coordination overhead has no offsetting benefit at this stage.
- **Trunk-Based Development, no GitFlow.** Correct for a solo/small team: `develop`/`staging` long-lived branches would only add merge overhead with no corresponding parallel-team benefit.
- **Conventional Commits.** Adopted verbatim — enables automated changelogs and keeps a readable history, at zero process cost.
- **Mandatory PR-to-self, even solo.** Adopted verbatim, and already anticipated by the Day-Wise Plan itself (Day 9's task explicitly says "use PR-to-self discipline for traceability") — the supplied document's reasoning (permanent record of design intent, ADR linkage, review evidence) is sound and directly serves the audit/traceability requirements repeated throughout the PRD (Ch.6.11, 7.14, 8.14).
- **Semantic Versioning for releases (vMAJOR.MINOR.PATCH).** Adopted, and it aligns with the unified versioning philosophy already decided in Day 6 v1.1 (API Standards).
- **Requirement traceability on every PR** (PRD chapter / Day-Wise Plan day / ADR / user story). Adopted — this is the single most valuable recommendation in the supplied document given this is a regulatory platform, and it costs nothing beyond template discipline.

### Adopted with correction

- **Repository structure: bounded-context-first, not layer-first.** The supplied document organizes `src/` by bounded context (ShipmentManagement, Declaration, Duty, etc.), each presumably layered internally. **Day 8's Coding Standards v1.0 had proposed the opposite** — one project per architectural layer (`CustomsClearance.Domain`, `.Application`, `.Infrastructure`, `.Api`) spanning all contexts. On review, the supplied document's structure is correct and Day 8's is revised here: a single cross-context `Domain` project would mix 17 bounded contexts' entities into one assembly, undermining the ownership boundaries established across Days 1-5 (each context owns its own aggregates; other contexts consume via events or read models, not direct references — Day 1 Section 4.1). Bounded-context-first modules, each internally layered, keep those boundaries enforceable at the project/namespace level, not just by convention.
- **Flyway for database migrations — replaced with EF Core Migrations.** The supplied document's Definition of Done and CI/CD sections assume Flyway-based migration validation. This conflicts with ADR-0001's binding constraint that EF Core is the sole data-access layer (chosen specifically for its PostgreSQL/SQL-Server portability path). Introducing Flyway alongside EF Core would create two competing migration mechanisms for the same schema. **Corrected decision: EF Core Migrations**, with the same DoD intent (migration reviewed, rollback considered) preserved.
- **AsyncAPI — adopted, but scoped as a Day 9+ addition, not backfilled onto Day 5/7.** The supplied document references AsyncAPI validation as a CI gate. This is a good idea not previously decided: Day 5 produced a domain event catalog in prose/table form, and Day 6 v1.1 defined the event envelope, but no formal AsyncAPI contract file was produced. Adding AsyncAPI specs (mirroring the `openapi/` folder from Day 7) gives the event catalog the same machine-readable, versionable treatment as the REST APIs. This is added to the repo structure below as `asyncapi/`, to be populated context-by-context as each service's events are implemented (starting Phase 0/Phase 3), not retroactively authored today.
- **Work-item ticket keys (e.g. `SHIP-101`, `DECL-052`) — replaced with a lightweight internal scheme.** The supplied document assumes an external issue tracker (Jira-style keys). This project has no such tool (2-person team, no ticketing system per `project_setup` memory). Corrected: branch/commit references use a lightweight `{CONTEXT-CODE}-{sequential-number}` key tracked in `docs/WORK-ITEMS.md` (a plain log, not a tool), preserving the traceability benefit without introducing external tooling overhead disproportionate to team size.

### Not adopted / deferred

- **CODEOWNERS-based code ownership by team.** Explicitly deferred — there is only one developer; the supplied document itself frames this as a later-team-growth concern, so no action is needed now. Revisit if/when the team grows beyond the user.
- **Full CI/CD gate list (security scan, dependency vulnerability scan, contract tests, etc.) as an immediate Day 9 action.** These are correctly the subject of Phase 0, Days 11-15 (CI/CD & Quality Gates) per the existing Day-Wise Plan, not Day 9 (repo/branch setup only). The supplied document's CI/CD expectations are noted here as forward input to that later phase, not implemented today.

## Final Decision Table

| Area | Decision |
|---|---|
| Repository | Monorepo |
| Structure | Modular Monolith, bounded-context-first (corrects Day 8) |
| Branching | Trunk-Based Development, short-lived feature branches |
| Main branch | `main`, protected, always releasable, no direct commits |
| Release branches | `release/*`, only when preparing a production release |
| Branch naming | `feature/{context-code}-{seq}-{slug}`, `bugfix/...`, `hotfix/...`, `refactor/...`, `spike/...`, `docs/...`, `test/...` |
| Work-item keys | Lightweight `{CONTEXT-CODE}-{seq}` logged in `docs/WORK-ITEMS.md` (no external tool) |
| Commit convention | Conventional Commits |
| Pull requests | Mandatory, including solo PR-to-self; template requires PRD chapter / Day-Wise Plan day / ADR linkage |
| Database migrations | EF Core Migrations (not Flyway) |
| API contracts | `openapi/` (Day 7, REST) + `asyncapi/` (new, events) |
| Releases | Semantic Versioning, `vMAJOR.MINOR.PATCH` |
| Code ownership | Shared (solo) for now; CODEOWNERS deferred |
| CI/CD gates | Deferred to Phase 0, Days 11-15, per existing plan |

## Consequences

- Day 8's proposed solution structure is superseded by the bounded-context-first structure in Section "Repository Structure" of the Day 9 deliverable — this is a correction made before any code was written, so no rework cost is incurred.
- Every future feature branch and PR must reference a work-item key from `docs/WORK-ITEMS.md`, keeping the Requirement -> ADR -> Implementation -> PR traceability chain intact without adopting a third-party tracker.
- `asyncapi/` grows alongside `openapi/` as each context's events are implemented, keeping REST and event contracts on equal documentation footing.
- Any future team growth triggers revisiting CODEOWNERS assignment, using the bounded-context boundaries already established (Day 1) as the natural ownership split.

## Related

[[ADR-0001]] (stack — EF Core mandated, hence Flyway not adopted). [[ADR-0002]] (rules engine — RulesEngine remains its own module in the structure below). Day 1 bounded contexts (17 total). Day 5/Day 6 v1.1 (event catalog and versioning — now given AsyncAPI contract treatment). Day 7 (`openapi/` precedent for `asyncapi/`). Day 8 Coding Standards v1.0 (structure section superseded).
