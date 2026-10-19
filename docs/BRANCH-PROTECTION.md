# Branch protection for `main`

**Status: NOT ENABLED.** As of Day 43 nothing on GitHub enforces the rules below; `main` accepts direct
pushes. The pipeline (Day 43, ADR-0038) is ready to be the enforcement mechanism, but turning protection on
is a GitHub *settings* change that the Product Owner makes deliberately. Nothing in this repo runs the
commands below automatically.

Repo: `muralensp/CustomsClearanceNextGenApp` (public).

## 1. What the gates are

| Required check (exact name) | Source | What it covers |
|---|---|---|
| `CI passed` | job `ci-passed` in `.github/workflows/ci.yml` | Aggregate of: Build, Lint, Unit Tests + Coverage Gate, Integration Tests, Containerize smoke, API contract tests, E2E smoke, Dependency Scan, Secret Scan. Fails if **any** of them is failure / cancelled / skipped. |
| `Analyze (csharp)` | `.github/workflows/codeql.yml` | CodeQL SAST. Separate workflow, so it cannot be folded into `CI passed`; require it explicitly. |

Why one aggregate name: branch protection matches check names literally. If every job were required
individually, adding/renaming a job would silently un-require it. Requiring `CI passed` means new gating jobs
only need to be added to its `needs:` list (and to `CIPassed` in `build/azure-pipelines.yml`).

A required check must have run at least once in the repo before GitHub's UI offers it by name; the API call
below accepts the strings regardless, but a typo makes every PR wait forever for a check that never reports.
So **do the demonstration in section 3 first** (it also makes both names appear), then enable protection.

## 2. Enabling protection (DO NOT RUN until the owner decides)

Requires `gh auth login` as the repo owner/admin.

```bash
gh api -X PUT repos/muralensp/CustomsClearanceNextGenApp/branches/main/protection \
  -H "Accept: application/vnd.github+json" \
  --input - <<'JSON'
{
  "required_status_checks": {
    "strict": false,
    "contexts": ["CI passed", "Analyze (csharp)"]
  },
  "enforce_admins": true,
  "required_pull_request_reviews": {
    "required_approving_review_count": 0,
    "dismiss_stale_reviews": true,
    "require_code_owner_reviews": false
  },
  "restrictions": null,
  "required_linear_history": true,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "required_conversation_resolution": false
}
JSON
```

Verify afterwards (read-only):

```bash
gh api repos/muralensp/CustomsClearanceNextGenApp/branches/main/protection \
  --jq '{checks: .required_status_checks.contexts, admins: .enforce_admins.enabled, force_push: .allow_force_pushes.enabled, delete: .allow_deletions.enabled}'
```

Undo (if it was a mistake):

```bash
gh api -X DELETE repos/muralensp/CustomsClearanceNextGenApp/branches/main/protection
```

### Choices in that payload, and why

| Setting | Value | Reasoning |
|---|---|---|
| Required checks | `CI passed`, `Analyze (csharp)` | See section 1. |
| Pull request required | yes (the `required_pull_request_reviews` block) | CONTRIBUTING.md has always said "every change goes through a PR, even PR-to-self"; this makes it true. |
| Approvals required | **0** | GitHub never lets the PR author approve their own PR, so `1` would make the solo owner unable to merge anything. The *automated gates* are the reviewer; the PR template checklist is the self-review record. Raise to 1 when a second human joins. |
| `strict` (branch must be up to date) | false | Solo trunk-based flow: the extra rebase-and-rerun cycle (~10 min of CI) buys little when there is one committer. Turn on if parallel PRs start colliding. |
| `enforce_admins` | **true (recommended)** | The owner is the admin and currently pushes straight to `main`. With `false`, protection would apply to everyone *except* the only person who commits, i.e. it would enforce nothing and the "blocks a broken PR" claim would be untrue. With `true` the owner goes through a PR like everyone else; the cost is ~10 min CI latency on a hotfix. Escape hatch for a genuine emergency, deliberate and visible in the audit log: `gh api -X DELETE repos/muralensp/CustomsClearanceNextGenApp/branches/main/protection/enforce_admins`, push, then re-enable with `-X POST` on the same path. |
| Force-push / deletion | both blocked | History of a regulatory platform's `main` must not be rewritable (ADR-0004 audit posture). |
| Linear history | true | Matches trunk-based development (ADR-0003); merge PRs with *squash* or *rebase* (disable merge commits in repo settings if wanted). |
| Restrictions (who may push) | none | Unneeded for one person; with `enforce_admins` the PR requirement already applies to everyone. |

Alternative: GitHub *rulesets* (Settings > Rules) can express the same thing and allow a named bypass actor
instead of the all-or-nothing `enforce_admins`. Classic protection was chosen only because it is a single
copy-pasteable command; rulesets are a reasonable later move.

## 3. Demonstration that the pipeline blocks a broken PR (DO NOT RUN as part of Day 43)

Purpose: prove, on real GitHub, that a deliberately broken PR cannot merge. Day 43 proved the *gate scripts*
fail locally (see `Day43_Execution_Log.md`); this proves the *wiring* (workflow -> `CI passed` -> merge button),
which can only be observed on GitHub.

Do this **before** enabling protection (so the check names exist), then repeat step 6 after enabling it.

1. Branch off up-to-date `main`:
   `git switch main && git pull && git switch -c test/ci-gate-demo`
2. Make one cheap, unambiguous break. Easiest: a failing unit test, e.g. in any `tests/Unit/**` test class add
   `[Fact] public void Demo_BrokenOnPurpose() => Assert.True(false);`
   (Other options, one per gate: a badly formatted line -> Lint; a hard-coded realistic-looking token, e.g. `ghp_` followed by 36
   random alphanumerics (do NOT use a real one) -> Secret Scan (note: an `...EXAMPLE` AWS key may be ignored by
   gitleaks' own stopwords, so use a random-looking value); a
   `<PackageReference Include="System.Net.Http" Version="4.3.0" />` in a `src/` project -> Dependency Scan.)
3. Commit and push: `git commit -am "test(ci): deliberately broken change" && git push -u origin test/ci-gate-demo`
4. Open the PR: `gh pr create --base main --title "DEMO: CI must block this" --body "Deliberately broken; do not merge."`
5. Watch: `gh pr checks --watch`. Expected: the affected job (e.g. "Unit Tests + Coverage Gate") is red, **`CI passed` is red**
   (its log lists the failing job), the other jobs may be green.
6. With protection enabled, the PR's merge button is disabled ("Required status check CI passed is failing").
   Confirm with `gh pr merge --squash` — it must be refused. (Without protection it would succeed; that is exactly
   why section 2 exists.)
7. Clean up without merging: `gh pr close --delete-branch`, then `git switch main && git branch -D test/ci-gate-demo`.
8. Record the PR number and the red `CI passed` run URL in the execution log or a follow-up ADR note.

Also worth one *positive* check: open a trivial docs-only PR and confirm `CI passed` goes green and the PR is
mergeable, so the gate is shown to pass as well as fail.

## 4. Known limits (be honest about what this does not do)

- `CI passed` only vouches for jobs listed in its `needs:`. A new gating job that is not added there is not
  enforced. (Same for `CIPassed` in `build/azure-pipelines.yml` after any migration.)
- The secret scan sees committed content only. A secret that is pushed then force-removed from history is still
  in GitHub's reflog/forks; rotate it, don't just delete it.
- CodeQL is GitHub-only (ADR-0038); it disappears if CI moves to Azure DevOps.
- Fork PRs: workflows run with a read-only token, which is sufficient for every gate here (none needs secrets).
