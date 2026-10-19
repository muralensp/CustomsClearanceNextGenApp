# ADR-0039: Phase 0 documentation structure (Day 44)

**Status:** Accepted
**Date:** 2026-10-19
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Day 44 calls for Phase 0 architecture documentation and runbooks (run locally, deploy, add a new service).
Two facts shaped the form: (1) the system is a modular monolith with no hosting target (ADR-0003, ADR-0008,
ADR-0015), so "how to deploy" and "add a new service" cannot be taken literally; (2) Days 41-43 were running
in parallel and would change security and CI/CD facts.

## Decision

1. Documentation lives under `docs/`: `Architecture/README.md` (components, module map, data, flows, ports),
   `C4/README.md` (context and container diagrams as Mermaid flowcharts, not the experimental C4 syntax),
   and `runbooks/` (`RUN-LOCALLY.md`, `ADD-A-MODULE.md`, `DEPLOY.md`).
2. "Add a new service" is reinterpreted as "add a module" (an `IApiModule` plus a platform library), per
   ADR-0015 and ADR-0035.
3. `DEPLOY.md` documents what exists and lists open decisions; it contains no invented procedure.
4. Content depending on Days 41-43 carries the marker `RECONCILE-AFTER-41-43` in an HTML comment, to be
   filled by a final reconciliation pass.
5. Runbooks are verified by executing them (stack run, scripts, a throwaway module that was then removed).

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Write docs after Days 41-43 finish | No placeholders | Serialises work, loses parallelism | Pilot goal is parallelism |
| Word documents like earlier days | Consistent with Phase -1 | Not diffable, not reviewable in PRs, not rendered on GitHub | Mermaid in Markdown renders on GitHub and lives with the code |
| Write a deployment procedure for a guessed target | Looks complete | Unverifiable, would be fiction | Working agreement: nothing is done until proven |

## Consequences

Docs must be updated when modules, ports or pipelines change (the add-a-module checklist includes this).
Until reconciliation, placeholders mark the unknowns. Revisit when a hosting target is chosen.

## Related

[[ADR-0003]], [[ADR-0008]], [[ADR-0015]], [[ADR-0035]].
