# ADR-0005: CI Platform Strategy — GitHub Actions Now, Azure DevOps Migration-Ready

**Status:** Accepted
**Date:** 2026-07-25
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Day 11 of the Day-Wise Plan requires a CI pipeline: build, lint, unit-test stages. The user specified starting on GitHub Actions, with an explicit intent to migrate to Azure DevOps later in the project, and asked that the planning stay adaptable to that migration.

## Decision

Adopt **GitHub Actions** as the active CI platform for now. All actual build/lint/test logic is implemented as portable shell scripts in `build/scripts/` (`build.sh`, `lint.sh`, `test.sh`), invoked identically by both `.github/workflows/ci.yml` (active) and `build/azure-pipelines.yml` (parked, not registered anywhere, kept in sync as a migration-readiness artifact).

## Rationale

- Keeping the actual logic (`dotnet restore/build/test/format`) in scripts rather than embedded directly in CI-platform YAML means migration is a matter of re-hosting orchestration syntax, not rewriting build logic. This is the concrete mechanism behind the "adaptable to Azure DevOps migration" requirement — not just an aspiration, but a structural constraint on how CI is authored from Day 11 onward.
- Both CI files use `ubuntu-latest`/`ubuntu-latest`-equivalent Linux runners and bash steps — bash is supported natively by both GitHub Actions and Azure Pipelines (`Bash@3` task), avoiding a PowerShell/bash fork that would need maintaining in two places.
- The parked Azure Pipelines file is **not** aspirational documentation — it was written and structurally mirrors the active GitHub Actions workflow stage-for-stage, so when migration actually happens, it needs only registration in Azure DevOps, not authoring from scratch.

## Consequences

- Any new CI stage (e.g. Phase 0 Day 12's coverage/SAST gates, Day 13's CD pipeline) must be added as a new script in `build/scripts/`, then wired into **both** `ci.yml` and `azure-pipelines.yml` in the same change — keeping them in permanent lockstep is now a discipline the Definition of Done (`CONTRIBUTING.md`) should account for.
- If this discipline lapses (a stage added to GitHub Actions only), the Azure DevOps file will silently drift out of date and the migration will cost more than intended. Flagged here as the risk this ADR is explicitly trying to prevent.
- Secrets/variables (e.g. NuGet feed credentials, once needed) must be referenced through each platform's own secret store (GitHub Secrets vs. Azure DevOps Variable Groups) — this is the one part of CI configuration that cannot be made portable via scripts, and will need re-entry at migration time.

## Related

[[ADR-0001]] (stack — .NET 8 SDK version pinned in both CI files). [[ADR-0003]] (repo structure — CI gates referenced there are deferred to Phase 0 Days 11-15, of which this ADR covers Day 11 specifically). Day 8 Coding Standards v1.0 (`.editorconfig`/`Directory.Build.props` — enforced by the lint stage).
