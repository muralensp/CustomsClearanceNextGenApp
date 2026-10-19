# Multi-agent pilot - metrics report (Days 41, 43, 44)

Written 2026-10-19 after the pilot was integrated. Method: `Multi_Agent_Approach.docx` and its review
(ADR-0035 foundation, `docs/PARALLEL-WORK.md`). Three worktree agents built Day 41 (security), Day 43 (CI/CD) and
the Day 44 documentation skeleton in parallel; Day 42 was deliberately run afterwards, alone (it needs an idle
machine and Day 41's result), so it is **not** part of this pilot.

Every number below is from a recorded source (agent transcripts, git history, GitHub run records) unless it is
marked **estimate**. Sources are listed in section 8.

## 1. Short answer

- **Active working time was cut roughly in half, not more.** The three workers ran in one concurrent window of
  about 49 minutes plus about 3 minutes to finish; integrating and verifying their output took about 27 more
  minutes. That is about 80 minutes of active work. Run one after another, the same three pieces would have taken
  an **estimated** 2.5 to 3 hours. The ratio, about 1.5 to 2 times, matches the 1.5x predicted before the pilot,
  and is well short of the "2x or more" the approach document hoped for.
- **Calendar time did not improve at all: 16 hours 11 minutes**, almost all of it (14 h 50 min) the hold and the
  overnight wait for the human. The method does not help while the one human decision-maker is the bottleneck.
- **Zero merge conflicts**, and the integrated tree passed everything (build, 12 unit projects, 14 integration
  tests, container smoke, E2E 20/20; CI 11/11 plus CodeQL on GitHub).
- **No sequential baseline was measured**, so the speed-up is a derived estimate, and the cost of the
  *foundation* work that made parallel work possible is not in it (section 6).
- **Agent self-reports were wrong about time.** Each worker reported 1.5 to 3 hours; the transcripts show about
  49 minutes. Evidence should come from the transcripts and the repo, not from a worker's summary.

## 2. Timeline (UTC)

| When | Event |
|---|---|
| 2026-10-18 11:38:40 | Pilot start recorded; three workers launched at 11:39:42 |
| 12:28:38 | User said "hold the activity"; all three workers stopped (48.9 min after launch) |
| *14 h 50 min pause* | Waiting for the user (hold, then overnight) |
| 2026-10-19 03:18 to 03:23 | Workers resumed after the user's approval and finished (C 03:19, B 03:22, A 03:23) |
| 03:23 to 03:45 | Orchestrator review, merge, verification, docs reconciliation, commit, push (22 min) |
| 03:45:32 to 03:50:16 | CI on the integration push: 11 of 11 jobs passed, CodeQL passed (4 min 44 s) |

Elapsed launch to CI-verified: **16 h 11 min**. Active: 48.9 + about 3.5 + about 27 = **about 80 minutes**.

## 3. The six metrics

### 3.1 Wall-clock to integrated and verified

| | Active | Elapsed |
|---|---|---|
| Parallel phase 1 (all three at once) | 48.9 min | 48.9 min |
| Worker finishing after resume | 1.0 to 3.4 min each | - |
| Orchestrator integration to CI green | about 27 min | - |
| **Total** | **about 80 min** | **16 h 11 min** |
| Sequential equivalent (**estimate**) | 2.5 to 3 h | - |

The sequential figure is derived: three workers each active about 49 minutes, summed (147 min) plus finishing
(about 7 min). It assumes no slowdown from sharing one machine when run together (not measured; A and C each
ran a full stack at the same time on 8 cores) and no saving from one agent reusing another's context. Both
could move it by tens of percent. It is an estimate, not a measurement.

### 3.2 Cost

Tokens only; no dollar figure is computed. Totals across both phases, from each worker's transcript:

| Worker | Tool calls | Output tokens | Cache-read tokens | Cache-write tokens |
|---|---:|---:|---:|---:|
| A (Day 41) | 121 | 25,274 | 33.0 M | 0.89 M |
| B (Day 43) | 80 | 23,635 | 14.9 M | 1.02 M |
| C (Day 44) | 73 | 36,967 | 16.1 M | 1.57 M |
| **Workers total** | **274** | **85,876** | **64.1 M** | **3.48 M** |

Cache reads dominate (each tool call re-reads a long context). The orchestrator's own usage across the same
period is not separated from the rest of the session and is not measured here. A sequential run would have had
one agent carrying one context, so its token total is likely lower; that is an expectation, not a figure.

### 3.3 Conflicts and rework

- **Textual merge conflicts: 0 of 3.** B fast-forwarded, C and A merged cleanly. The ownership table in
  `docs/PARALLEL-WORK.md` plus the module refactor did what they were for: the three workers touched disjoint
  files except a handful of additive lines.
- **Semantic overlaps that git could not see (all found and fixed by the orchestrator):**
  - A's security checklist listed secret scanning and a vulnerable-package gate as open items; B built both.
  - C's docs were written before A and B finished, so some statements went stale: the ten reconcile markers
    (8 filled, 2 for Day 42) and three more that no marker covered (Dockerfiles said to bake in `Development`,
    where schema bootstrap lives, "no secrets to create").
  - C found a real gap in `docs/PARALLEL-WORK.md`: a new module also needs `ApiModuleCatalogTests` edited.
- **Interruptions:** the hold stopped all three mid-work; a leftover compose stack (10 containers) kept running
  after the kill; resuming two of the three workers was blocked once by the permission system and needed a
  second approval from the user.
- **Environment friction hit by every worker:** worktrees on Windows check out CRLF files, so `dotnet format
  --verify-no-changes` fails with end-of-line errors on every file. Fixed afterwards with `.gitattributes`.
  Workers also could not run `eval "$(agent-env.sh N)"` in their sandbox and used workarounds.
- **Worker-found and self-fixed:** A found both Dockerfiles baked in the `Development` environment (a real
  security finding) and that the first OpenTelemetry upgrade (1.11.2) was still flagged; B found 3 genuine High
  advisories in test-only packages and pinned them; C corrected its own draft after running the steps.

### 3.4 First-pass success

All three delivered a complete result on the first attempt; the orchestrator sent none of them back with a
change request. All three passed on the merged tree without fixes to their code. The caveat: that is partly
because the review was light (3.5), and A and B were resumed by the orchestrator after being stopped, which is
not a clean first pass in the ordinary sense.

### 3.5 Review defects

- **Code (A, B):** the orchestrator read both diffs and found no blocking defect. Both are covered by tests and
  by the integrated-tree checks below.
- **Docs (C):** **not reviewed line by line.** The orchestrator relied on C's report, then on the reconcile pass
  that touched the same files. The three stale statements in 3.3 were found that way.
- **Post-merge defects:** none found by CI or by the later branch-protection demonstration.
- **A weakness of the review itself:** the workers' reports contained claims that the transcripts contradict
  (their own durations). Checking was done on outputs (builds, tests, runs), not on the narrative.

### 3.6 Integration time

About 22 minutes from the last worker finishing to a pushed, reconciled `main`, plus 4 min 44 s of CI, so about
27 minutes. It covered reading the diffs, committing C's and A's work on their branches, merging, a full build,
dependency scan, unit and integration tests, container smoke and E2E on the merged tree, then the docs
reconciliation. This is the cost that grows with the number of parallel workers.

## 4. What was verified on the integrated result

Build 0 warnings (A's OpenTelemetry upgrade cleared the old NU1902); dependency scan exit 0 with 0 blocking and 0
warnings; 12 unit-test projects exit 0 with coverage gates met; 14 integration tests; `smoke-test.sh` exit 0
including the new `audit_writer` check; E2E 20/20 with evidence file; on GitHub CI 11/11, CodeQL
`Analyze (csharp)`, and later a deliberately broken PR was shown blocked once branch protection was enabled.

## 5. What worked

- **Disjoint files.** Documentation, CI and security code rarely touch the same lines. With `Program.cs` already
  split into modules, that held: zero conflicts.
- **Slot isolation.** Separate compose projects and port ranges let two workers run full stacks at the same time.
- **Machine-checkable evidence** (unpiped exit codes, evidence files) made the integrated verification quick and
  made the workers' narrative mostly unnecessary.
- **Workers finding real defects** (the Dockerfile environment default, three High advisories) that a narrower
  brief would likely have missed.

## 6. What it cost and what the numbers leave out

- **The foundation was not free.** Splitting `Program.cs` into modules, parameterising compose, writing
  `agent-env.sh` and `docs/PARALLEL-WORK.md`, and ADR-0035 were done before any worker started, in this same
  session. That effort is **not measured and not included** in the 80 minutes. It is reusable for later parallel
  work, but a fair comparison charges it to the pilot.
- **Machine is the shared resource.** Everything ran on one 8-core, 7.6 GiB Docker host. More than two or three
  workers running stacks at once would hit that ceiling (documented in `docs/PARALLEL-WORK.md`).
- **The human is the real bottleneck.** The user had to answer design decisions, approve resumes and merges, and
  the hold itself stopped everything. Parallel agents do not shorten any of those waits.
- **Agent reports need verifying.** The self-reported durations were about two to four times the recorded
  ones. Anything a worker states that matters should be re-checked from outputs.

## 7. Recommendation

1. **Use parallel workers selectively**: independent pieces over disjoint files, with an up-front ownership table
   and reserved ADR numbers, as here. The gain is real but modest (about 1.5 to 2 times on active time), and it
   shrinks once foundation and integration are counted.
2. **Do not use it for strict build chains** such as Days 31 to 35, or for work that needs an idle machine
   (Day 42 was correctly kept out).
3. **Keep the controls that worked**: isolated slots, evidence files, unpiped exit codes, the orchestrator-owned
   shared files, merging only after re-verifying on the merged tree.
4. **Do not scale to per-bounded-context agent teams yet.** One pilot with an estimated baseline is thin
   evidence. If you want to decide with data, time one comparable independent task done sequentially next to a
   parallel one, from a clean start, before committing to more.
5. **Cheap improvements regardless**: `.gitattributes` is done; consider having the orchestrator, not a worker,
   own the cross-cutting docs reconciliation, since that is where the semantic overlaps appeared.

## 8. Sources and limits

- **Worker times, tool calls and tokens**: the three agent transcripts (timestamps of first and last message per
  phase; `usage` fields summed). Worker self-reported durations were not used.
- **Pilot start**: `pilot_start.txt` (2026-10-18T11:38:40Z). **Merge and push times**: `git log` (commit
  `eb26dfe` pushed 2026-10-19 03:44Z). **CI**: GitHub run 39196201890 (created 03:45:32Z, updated 03:50:16Z).
- **Not measured**: a sequential baseline, the foundation effort, the orchestrator's own token use, dollar cost.
- **A weak signal, not used**: commit spacing for Days 36 to 40 was considered as a sequential baseline and
  rejected, because those days were committed in batches when the user asked, so the gaps are not work time.
- One pilot, three tasks, one machine, one person: treat every ratio here as indicative.
