# ADR-0038: CI/CD Hardening - Automatic Quality Gates Before Merge

**Status:** Accepted
**Date:** 2026-10-19
**Deciders:** Product Owner (decision pending on enabling branch protection); Claude (Worker B, Day 43)

## Context

Plan wording (Day 43): "Ensure CI/CD pipeline enforces all quality gates automatically (coverage, lint, security scan) before merge"; deliverable "Pipeline blocks a deliberately broken PR".

An audit (Day43_Execution_Log.md) found CI already enforced build, lint, unit tests with per-project coverage thresholds, integration tests and three docker-based smoke/contract/E2E layers, but ADR-0004 item 16, ADR-0007 and Gate 2/Gate 5 promise more: **no dependency (SCA) scan, no secret scan, no CodeQL/SAST beyond build-time SonarAnalyzer**, and no single required check name. The repository is now **public**, which raises the stakes of committed secrets and makes CodeQL free. CONTRIBUTING.md claimed `main` was protected; it is not.

The first dependency scan also found **real High advisories on the existing tree** (test-only transitive packages `System.Net.Http` 4.3.0, `System.Text.RegularExpressions` 4.3.0, `System.Text.Json` 8.0.4).

## Decision

1. `build/scripts/dependency-scan.sh`: `dotnet list <sln> package --vulnerable --include-transitive --format json`, parsed with python. High/Critical (and unknown severity) fail; Moderate/Low print a warning. Fails closed (exit 2) if NuGet could not supply advisory data (NU1900) or nothing was scanned. Time-boxed exceptions live in `build/dependency-scan-allowlist.txt` (GHSA id, expiry date, reason; expired entries stop applying). The list is empty: the Day 43 findings were **fixed** by pinning patched versions in a new `tests/Directory.Build.props` (which imports the root props), not excepted.
2. `build/scripts/secret-scan.sh`: gitleaks over **full history** of a throwaway clone, using a local binary if present else a **pinned docker image** (`zricethezav/gitleaks:v8.21.2`). Docker was chosen over a GitHub Action because the same script then runs unchanged on Azure Pipelines, on a developer machine and in CI (ADR-0005 portability), and gitleaks' own Action needs a licence key for organisation repos. `.gitleaks.toml` extends the default rules and allowlists, by exact value, only the two committed LOCAL-DEV-ONLY placeholders (the JWT dev key and the `devlocalonly` Postgres password). Default rules did not flag them anyway; the allowlist is a guard against rule changes, kept narrow so a real secret in the same files still alerts. A shallow checkout only scans fetched history, so CI uses `fetch-depth: 0`.
3. `.github/workflows/codeql.yml`: CodeQL for C#, `build-mode: none`, `security-extended`, on PR, push to main and weekly.
4. `.github/dependabot.yml`: nuget, github-actions, docker (Api, Gateway Dockerfiles) and docker-compose, weekly, majors ignored for NuGet (deliberate decisions).
5. `ci.yml` gains `dependency-scan`, `secret-scan` and an aggregate job named **`CI passed`** (`if: always()`, `needs:` every gating job, fails unless every needed result is `success`, so failure/cancelled/skipped all fail). `azure-pipelines.yml` gains matching `DependencyScan`, `SecretScan` and `CIPassed` stages (lockstep, ADR-0005). Top-level `permissions: contents: read` added to ci.yml.
6. **Deliberate lockstep exception:** CodeQL has no Azure Pipelines equivalent (GitHub-hosted service). On migration to Azure DevOps it is replaced by GitHub Advanced Security for Azure DevOps or another SAST tool. Noted in both YAML headers.
7. Branch protection is **documented, not enabled** (`docs/BRANCH-PROTECTION.md`): required checks `CI passed` + `Analyze (csharp)` (CodeQL is another workflow so cannot be inside the aggregate), PR required, 0 approvals (a solo author cannot approve their own PR), no force-push/deletion, **`enforce_admins: true` recommended** because the sole committer is the admin and otherwise protection would bind no one. The owner decides when to run it; CONTRIBUTING.md now states the interim truth.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Require each job as its own check | No aggregate job | Renaming/adding a job silently un-requires it | One stable name is safer |
| gitleaks GitHub Action | No docker | Licence key for orgs; not portable to Azure; not locally runnable | Docker script is portable |
| Allowlist the test-package advisories | Quick | Permanent exceptions erode the gate | They were fixable with three pins |
| Fail on Moderate too | Stricter | Would fail on the known OpenTelemetry 1.9.0 Moderate advisories that cannot be fixed without breaking the pinned Prometheus beta exporter (ADR-0022) | Moderate = visible warning, per brief |
| Dependabot only (no scan script) | Zero script | Not a merge gate | Dependabot complements, does not gate |
| Make CodeQL part of `CI passed` via `workflow_call` | One check | Doubles runs or forces restructure | Two required names is simple enough |

## Consequences

- A PR with a failing test, format violation, coverage drop, High/Critical vulnerable package or committed secret-like string now fails a named job and therefore `CI passed`. Proven locally at script level (log); the GitHub wiring is only observable once pushed (procedure in docs/BRANCH-PROTECTION.md section 3).
- **Until the owner runs the protection command, none of this blocks a merge or a direct push.** The gates are red-light signals, not locks.
- Developers can hit new failures from freshly published advisories without touching code; the answer is to fix or add a dated exception.
- New test projects automatically inherit the test pins via `tests/Directory.Build.props`; remove a pin when upstream stops pulling the vulnerable version.
- Findings carried forward, not fixed here (application code out of scope): OpenTelemetry.Api / Exporter.OpenTelemetryProtocol 1.9.0 Moderate advisories (ADR-0022, Day 41 may address); the working tree on Windows (`core.autocrlf=true`) holds CRLF while `.editorconfig` says LF, so `dotnet format --verify-no-changes` reports ENDOFLINE locally on Windows checkouts but is clean on LF checkouts (CI, Linux).
- Revisit when: CI moves to Azure DevOps (replace CodeQL), a second developer joins (raise approvals), or SonarCloud is provisioned (ADR-0007).

## Related

ADR-0004 (SCA/SAST requirement), ADR-0005 (lockstep), ADR-0007 (coverage/static analysis), ADR-0009, ADR-0034, ADR-0022 (OpenTelemetry advisory), `docs/quality-gates/Gate2_Backend_Review.md`, `Gate5_Release_Review.md`.
