# Day 43 Execution Log - CI/CD Hardening

Branch: `worktree-agent-ab978e5b1d1eccb78` (base 27acf47). ADR: `adr/0038-ci-cd-hardening.md`.
Deliverable: "Pipeline blocks a deliberately broken PR" - proven at gate-script level locally; GitHub wiring not executable here.

## 1. Audit - what CI enforced before Day 43 vs. what the docs require

| Control | Required by | CI before Day 43 | After Day 43 |
|---|---|---|---|
| Build | Gate 2 | Yes (`build` job) | unchanged |
| Lint / format | ADR-0007, Gate 2 | Yes (`dotnet format --verify-no-changes --severity warn`) | unchanged |
| Unit tests | Gate 2 | Yes | unchanged |
| Coverage thresholds (95% core / 80% CRUD) | ADR-0007 | Yes, enforced inside test.sh by coverlet (build fails below threshold) | unchanged |
| Integration / container / contract / E2E | ADR-0009, ADR-0034 | Yes (4 jobs) | unchanged |
| Static analysis (SonarAnalyzer) | ADR-0007 | Build-time warnings only, not a gate (TreatWarningsAsErrors=false) | unchanged (deliberate, per ADR-0007) |
| **Dependency / SCA scan** | ADR-0004 item 16, Gate 2, Gate 5 | **None** | `dependency-scan` job, fails on High/Critical |
| **Secret scan** | ADR-0004 ("never in source"), repo now public | **None** | `secret-scan` job, full history, gitleaks |
| **SAST (CodeQL)** | ADR-0004 "SAST + SCA in CI" | **None** | `codeql.yml` |
| Dependency update automation | good practice | None | `dependabot.yml` |
| Single required check for branch protection | brief | None (9 separate job names) | `CI passed` aggregate |
| Branch protection on `main` | CONTRIBUTING.md claimed it | **Not enabled** (claim was untrue) | Still not enabled; documented in docs/BRANCH-PROTECTION.md, CONTRIBUTING.md corrected |
| Azure DevOps lockstep | ADR-0005 | in lockstep | DependencyScan/SecretScan/CIPassed stages added; CodeQL = documented exception |

## 2. Findings made while building

1. First dependency scan of the existing tree: **3 High advisories** (test-only transitive): System.Net.Http 4.3.0 (GHSA-7jgj-8wvc-jh57), System.Text.RegularExpressions 4.3.0 (GHSA-cmhx-cq75-c4mj), System.Text.Json 8.0.4 (GHSA-8g4q-xg66-9fp4, Api.IntegrationTests only). Fixed with pins in new `tests/Directory.Build.props`; re-scan clean. Build, all unit tests and integration tests still pass with the pins.
2. Moderate (warn only): OpenTelemetry.Api 1.9.0 GHSA-g94r-2vxg-569j and OpenTelemetry.Exporter.OpenTelemetryProtocol 1.9.0 GHSA-4625-4j76-fww9, in Api, Gateway, Observability, Api.IntegrationTests. Script works whether or not they are present.
3. gitleaks default rules found nothing in the 32-commit history of the base tree; the placeholder allowlist is precautionary.
4. Windows worktree has CRLF working files (`core.autocrlf=true`) vs `.editorconfig` `end_of_line=lf`; `dotnet format --verify-no-changes` there reports ~8,880 ENDOFLINE errors on the untouched base tree (environmental; index is LF, CI on Linux is LF). CRLF also would break bash scripts in that worktree. All gate proofs were therefore run in an LF clone (`core.autocrlf=false`) of this worktree's repo, in the session scratch directory. Not an application-code problem; no src changed.
5. Harness note: git-flavoured strings in commands are blocked for worktree-isolated agents; irrelevant to the product.

## 3. Commands and unpiped exit codes

Worktree (`C:\...\worktrees\agent-ab978e5b1d1eccb78`, CRLF checkout):

| Command | Exit | Notes |
|---|---|---|
| `bash build/scripts/dependency-scan.sh` (before pins) | 1 | 3 High blocking, 2 Moderate warned |
| `bash build/scripts/dependency-scan.sh` (after pins) | 0 | 28 projects scanned, 0 blocking, 2 Moderate warnings |
| `bash build/scripts/secret-scan.sh` | 0 | "32 commits scanned ... no leaks found" |
| `bash -n` on dependency-scan.sh, secret-scan.sh | 0 | |
| `dotnet build CustomsClearance.sln` | 0 | 0 errors, 2 warnings (the known NU1902 OpenTelemetry Moderate) |
| `dotnet format CustomsClearance.sln --verify-no-changes` | 2 | ENDOFLINE only, CRLF checkout artefact, see finding 4 |
| YAML parse (python yaml) of ci.yml, codeql.yml, dependabot.yml, azure-pipelines.yml | OK | ci.yml jobs: build, lint, test, integration-test, containerize, contract-test, e2e-smoke-test, dependency-scan, secret-scan, ci-passed |

LF clone, unbroken tree (commit fe114a0 content):

| Command | Exit |
|---|---|
| `bash build/scripts/lint.sh` | 0 |
| `dotnet format CustomsClearance.sln --verify-no-changes` | 0 |
| `bash build/scripts/dependency-scan.sh` | 0 |
| `bash build/scripts/secret-scan.sh` | 0 |
| `bash build/scripts/test.sh` | 0 (371 s, all coverage thresholds met) |
| `bash build/scripts/integration-test.sh` | 0 (14/14 passed) |

## 4. Broken-PR proof (local; each failure mode as its own commit on a throwaway branch from the base, in the scratch clone, then discarded)

| Failure mode | Broken change | Gate script | Exit | Evidence |
|---|---|---|---|---|
| Failing unit test | `DeliberatelyBrokenTests.Demo_BrokenOnPurpose` asserts false (Audit.Persistence.Tests) | `test.sh` | **1** | `Failed DeliberatelyBrokenTests.Demo_BrokenOnPurpose` |
| Lint / format | `BadlyFormatted.cs` with bad spacing | `lint.sh` | **2** (dotnet format's code for violations; non-zero) | `error WHITESPACE: Fix whitespace formatting` (14 diagnostics) |
| Coverage drop | 60 uncovered methods added to `Platform.RulesEngine` (95% gate) | `test.sh` | **1** | `coverlet.msbuild.targets: error : The total line coverage is below the specified 95` for `Platform.RulesEngine.Tests` |
| Vulnerable package | `Newtonsoft.Json 9.0.1` in Platform.Common | `dependency-scan.sh` | **1** | `BLOCKING - High/Critical advisories (1): [High] Newtonsoft.Json 9.0.1 GHSA-5crp-9r3c-p9vr` |
| Committed secret-like strings | GitHub-PAT-shaped and AWS-key-shaped literals in a .cs file (random-looking, not real) | `secret-scan.sh` | **1** | `leaks found: 2`, rules `github-pat` and `aws-access-token`, values redacted |
| Unbroken tree | none | all of section 3 | **0** | |

Branch handling: the broken commits exist only in the scratch clone (branches `broken/<mode>`), never in this worktree's branch. The scratch clone is disposable; none of its commits were pushed anywhere.

## 5. Verified locally vs only when pushed

Verified locally by running: all four new/changed gate scripts (pass and fail), existing test/lint/integration scripts after the changes, YAML structure parse, `bash -n`.
NOT verifiable here (no `act`, nothing pushed): that `ci.yml` jobs run on GitHub as written (including the `jq` logic in `CI passed`, `actions/*` versions, docker availability for gitleaks on the runner, `fetch-depth: 0`); that `CI passed` turns red when a needed job fails; CodeQL `build-mode: none` for this solution and its check name `Analyze (csharp)`; dependabot config acceptance (esp. the `docker-compose` ecosystem and group patterns); Azure Pipelines syntax (never run anywhere). First real confirmation = first push / PR; procedure in docs/BRANCH-PROTECTION.md section 3 (not performed).

## 6. Not done / decisions for the Product Owner

- Branch protection NOT enabled (command in docs/BRANCH-PROTECTION.md). Decide: run it, and accept `enforce_admins: true`.
- Real GitHub demonstration PR not performed.
- OpenTelemetry Moderate advisories remain (warn only).
- CRLF vs LF checkout on Windows: consider a `.gitattributes` (`* text=auto eol=lf`) in a separate change; not touched here.
