# Quality Gate Checklists

Day 15 of the Phase -1 Day-Wise Plan: "Formalize Gate 1–5 checklists (Architecture, Backend, API, Frontend, Release) as living checklists to use at each phase exit." These five gates were first named — as short bullet lists — in `CustomsClearance_Phasewise_DevlopmentPlan_v2.docx`'s Quality Gates section and echoed in the reviewer checklist of `.github/pull_request_template.md`. This folder expands each into a concrete, checkable, reusable checklist, grounded in the actual standards this project has since established (ADRs 0001–0009, Days 1–14), not generic best-practice language.

## How These Are Used

- **Per-PR:** the relevant gate(s) for the PR's scope are copied into the PR description (or referenced by link) and checked off before merge — this is what `.github/pull_request_template.md`'s Reviewer Checklist section points to.
- **Per-phase-exit:** per the Phase-wise Plan v2's layering (Database → Backend → API → Frontend, then System/Release), the corresponding gate below is walked in full and signed off before the next layer/phase begins — this is the literal "phase exit" use case Day 15 asks for.
- **Living documents:** these are not one-time checklists. As new ADRs are added or standards change, update the relevant gate file in the same PR — a stale gate is worse than no gate, since it creates false confidence.

## The Five Gates

| Gate | Applies To | File |
|---|---|---|
| 1 | Architecture | [Gate1_Architecture_Review.md](Gate1_Architecture_Review.md) |
| 2 | Backend | [Gate2_Backend_Review.md](Gate2_Backend_Review.md) |
| 3 | API | [Gate3_API_Review.md](Gate3_API_Review.md) |
| 4 | Frontend | [Gate4_Frontend_Review.md](Gate4_Frontend_Review.md) |
| 5 | Release | [Gate5_Release_Review.md](Gate5_Release_Review.md) |

Gates 1–4 map onto the Day-Wise Plan's own per-phase layering (Layer 1 Database / Layer 2 Backend / Layer 3 API / Layer 4 Frontend, repeated per bounded context per the Phase-wise Plan v2). Gate 5 is the final release-readiness gate, run once per environment promotion (dev → staging → prod, per `adr/0008-cd-pipeline-and-environment-promotion.md`), not per bounded context.

## Sign-Off Block

Every gate file ends with a sign-off block:

```
**Reviewed by:** _______  **Date:** _______  **Scope (context/PR/release):** _______  **Result:** Pass / Pass with noted exceptions / Fail
```

Fill this in and keep a copy (e.g. pasted into the PR, or committed to `docs/quality-gates/history/` if a permanent record is wanted later) — for now, the PR itself is the record, consistent with the "PR-to-self, always" discipline from `adr/0003`.
