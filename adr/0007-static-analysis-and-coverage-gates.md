# ADR-0007: Static Analysis & Coverage Gates

**Status:** Accepted
**Date:** 2026-07-27
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Day 12 of the Day-Wise Plan requires integrating static analysis (SonarQube/equivalent) and coverage reporting, with thresholds of 95% for core business/rules logic and 80% for CRUD-heavy master-data services, per the Phase-wise Plan v2's revised quality gate (itself a v2 correction of a blanket 95% rule — see `CustomsClearance_Phasewise_DevlopmentPlan_v2.docx`).

No SonarCloud/SonarQube server account exists for this project, and standing one up is an external-service decision, not a Day 12 default. This ADR records what was actually implemented and verified locally, plus what remains a deliberate, named gap rather than an oversight.

## Decision

1. **Static analysis: `SonarAnalyzer.CSharp`** (NuGet package), referenced solution-wide via `Directory.Build.props`. This is a standalone Roslyn analyzer — it applies Sonar's C# rule set (bug patterns, code smells, security hotspots) at build time as compiler warnings, and requires **no SonarCloud/SonarQube server, account, or token**. A full SonarQube/SonarCloud server integration (dashboards, trend tracking, quality-gate history across runs) remains a later, optional upgrade, not done now.

2. **Coverage: `coverlet.msbuild`** (not `coverlet.collector`) per test project, with `Threshold`/`ThresholdType`/`ThresholdStat` declared as MSBuild properties **inside each test project's own `.csproj`**, not passed as CI command-line flags. This lets each project carry its own bar (95% core logic vs. 80% CRUD) without the CI script needing to know which project is which — the classification lives with the project, per the convention in the table below.

3. **Enforcement mechanism:** `dotnet test` (no `--no-build`) fails outright (non-zero exit code) if a project's coverage falls below its own declared threshold. Verified locally (Section "Findings" below) — this is a real gate, not a report a human has to remember to check.

## Threshold Classification Convention

| Context Type | Threshold | Applies To |
|---|---|---|
| Core business/rules logic | 95% line coverage | Segregation Engine, Duty & Tax Engine, Declaration lifecycle, Rules Engine (per ADR-0002), Compliance Engine |
| CRUD-heavy master data | 80% line coverage | Party/Customer Management, Administration, lookup-table-style services |
| Shared platform libraries (not yet classified elsewhere) | 80% line coverage (default) | `Platform.Common` and siblings, until/unless a given library grows business-rule-like logic of its own |

Each test project's `.csproj` must declare its tier explicitly (as `Platform.Common.Tests` now does) with a comment citing this ADR, so the threshold is never accidental or copy-pasted without thought.

## Findings From Local Verification (Non-Obvious, Worth Recording)

1. **`coverlet.collector` and `coverlet.msbuild` must not both be referenced in the same test project.** Running both produced an empty/misleading cobertura report (all packages present, zero classes tracked) with no error — a silent failure mode, not a loud one. Resolved by using `coverlet.msbuild` only, since threshold enforcement requires it.
2. **`dotnet test --no-build` silently skips coverlet.msbuild's instrumentation.** Instrumentation is hooked into `dotnet test`'s own build step; reusing a prior `dotnet build` output (even of the same code) produces an empty coverage report instead of an error. `build/scripts/test.sh` therefore does **not** pass `--no-build`, even though `build.sh` already built the solution earlier in the same CI job — a small redundancy traded for correctness.
3. **A class with only `const` fields has zero coverable lines**, and coverlet/ReportGenerator report this as a vacuous 100% (0-of-0), not an error or a 0%. This is correct tool behavior, not a bug, but it means a coverage report full of "100%" placeholder modules proves nothing — confirmed by deliberately adding a real method (`PlaceholderModuleMarker.Describe()`) and a covering test, which produced genuine non-vacuous coverage numbers.

## Consequences

- Every future test project must declare `CollectCoverage`, `CoverletOutputFormat`, `CoverletOutput`, `Threshold`, `ThresholdType`, `ThresholdStat` in its own `.csproj`, classified per the table above — this is now part of the Definition of Done for creating a new context module (to be reflected in `CONTRIBUTING.md`).
- `build/scripts/test.sh` runs `dotnet reportgenerator` to produce a human-readable HTML + text summary from all projects' cobertura outputs in one pass, uploaded as a CI artifact (`TestResults/`) — this gives a single aggregate view even though thresholds are enforced per-project.
- SonarAnalyzer warnings currently do not fail the build (`TreatWarningsAsErrors=false`, per ADR/Day 8) — they surface in the Build job's log for review, not as a hard gate. Revisit if warning-fatigue becomes a problem, or if specific rule categories (e.g. security hotspots) warrant promotion to errors.
- If/when a SonarCloud/SonarQube server is later adopted (a genuine external-service decision, not assumed here), it would run *alongside* SonarAnalyzer.CSharp (server-side aggregation/trends) rather than replacing it (build-time, zero-config local checks) — these are complementary, not competing, tools.

## Related

[[ADR-0005]] (CI platform strategy — both `ci.yml` and `azure-pipelines.yml` updated together per its discipline requirement). [[ADR-0001]] (stack — SonarAnalyzer.CSharp and coverlet.msbuild are both pure .NET/MSBuild tooling, no portability concern). Phase-wise Plan v2 (95%/80% threshold policy this ADR implements). Day 8 Coding Standards v1.0 (testing conventions this extends with concrete enforcement).
