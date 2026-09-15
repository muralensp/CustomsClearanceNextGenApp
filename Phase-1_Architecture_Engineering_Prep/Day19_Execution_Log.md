# Day 19 Execution Log — Rules-Engine Design Spike (Design Doc Phase)

Phase -1 (Architecture & Engineering Preparation), Day 19 of 20. Per the user's Day 19 instruction,
this day is split into two explicitly-gated steps: (1) the design document — captured here — and
(2) a throwaway prototype validating the design against real PRD scenarios, only after separate
confirmation. This log covers step (1) only.

---

## Step 1 — Re-ground in the prior decisions before designing

Re-read `adr/0002-configurable-rules-engine.md` (the config-driven decision this spike designs the
pipeline for) and `adr/0012-persistence-and-messaging-libraries.md` (the platform libraries this
engine sits on top of) to confirm scope: Day 19 designs the evaluation pipeline; Phase 0 Days 31-35
build it.

## Step 2 — Extract the PRD sections the pipeline must satisfy

`CustomsClearance_PRD.docx` is binary; extracted the relevant text directly (`python3` +
`zipfile`/regex over `word/document.xml`, no conversion tool needed) rather than guessing at
remembered content:

- **Ch.5.4 Inputs** — Shipment/Customer/Commodity/External Reference Data fact sources.
- **Ch.5.7 Rule Evaluation Pipeline** — the fixed, sequential stage order (Country → Customer →
  Shipment → Commodity → HS Code → Duty → Permit → Shipment Indicators → Warning → Final CT
  Assignment → Status Update). This turned out to be the single most load-bearing fact for the
  design: a fixed sequential order means later stages can depend on earlier stages' outcomes,
  which rules out a "frozen Facts snapshot evaluated once" design (see ADR-0013 Alternatives).
- **Ch.5.9 Rule Priority** — 1=Legal Restrictions (highest) … 9=Default Rules (lowest); higher
  priority overrides lower within a conflict.
- **Ch.5.10 Decision Table** — illustrative outcomes (DOM, FTZ, FML, DEM, ABK, INF) used as the
  candidate scenarios for the not-yet-started prototype step.
- **Ch.5.11-5.14** — Re-Segregation (attribute-change triggers), Manual Override (Supervisor-only,
  reason mandatory, audited), Audit & Explainability (per-rule DecisionTrace, including a
  "Not applicable" step for rules that didn't fire), and the Ch.5.14 acceptance criteria (rule
  outcomes must be deterministic and repeatable — this became the deciding factor in the priority-
  tiebreak design choice below).

Cross-checked against `Day3_ER_Diagram_v0.2.docx` §5 (Rule/RuleVersion/RuleCondition/DecisionTrace
field-level schema — extracted the same way) to make sure the pipeline design's types line up
exactly with the already-agreed data model rather than inventing a parallel shape.

## Step 3 — Design the five-stage pipeline

Landed on five composable interfaces (`IFactsProvider`, `IRuleSetLoader`, `IConditionEvaluator`,
`IPriorityResolver`, `IDecisionTraceRecorder`) plus a `RulesEngine.EvaluateAsync` orchestrator.
Full interface signatures and rationale are in `adr/0013-rules-engine-design-spike.md`. Key design
calls made, each because a prior artifact's language directly forced the answer:

1. **`Facts` mutated in place across pipeline stages, not frozen at the start.** Forced by PRD
   Ch.5.7's fixed sequential stage order — a Permit Rule needs `ControlledGoodsFlag` as set by an
   earlier HS Code Rule. Flagged explicitly as the one deliberate divergence from a naive
   "immutable facts snapshot" reading of Day 3 §5.4's `FactsSnapshot` field (that field captures
   the trace's snapshot *per step*, not one global frozen input).
2. **`IRuleSetLoader.LoadAsync` is called once per pipeline stage** (scoped by `RuleCategory[]`),
   not once for the whole rule table. Trades a few extra DB round-trips for a natural per-stage
   trace checkpoint. Flagged as a Day-31 performance question to revisit, not a final call.
3. **Priority ties broken deterministically by `RuleId` ordering, not by throwing.** Forced by PRD
   Ch.5.14's acceptance criterion that outcomes be deterministic and repeatable — an exception on
   tie would make evaluation non-deterministic in the face of a rule-authoring mistake. Tie
   *detection* is pushed to the Day 34 admin API's validation instead, where it can be caught at
   authoring time rather than at evaluation time.
4. **Pipeline stage order (PRD Ch.5.7) is hardcoded, not itself rule-configurable.** ADR-0002 makes
   the *rules* config-driven; this spike treats *stage order* as a product/architecture decision.
   Named explicitly as a tradeoff in ADR-0013 rather than left ambiguous.

## Step 4 — Record the decision

**File added:** `adr/0013-rules-engine-design-spike.md` — full interfaces, orchestration pseudocode,
alternatives considered (with reasons *why not*, not just what), and an explicit "out of scope"
list (Admin CRUD API, Re-Segregation event wiring, Manual Override, EF Core persistence
implementation — all deferred to Phase 0 Days 31-35).

---

## Summary of Files Changed/Added This Day (Design Doc Phase)

- `adr/0013-rules-engine-design-spike.md` — new
- `Phase-1_Architecture_Engineering_Prep/Day19_Execution_Log.md` — this file
- `Phase-1_Architecture_Engineering_Prep/Day19_Rules_Engine_Design_Spike.docx` — new (narrative
  deliverable, if requested)
- `CLAUDE.md` — updated (Day 19 row, Day-by-Day Deliverables table)

No code written this step — per the user's explicit request, the throwaway prototype validating
this design against PRD Ch.5.10 scenarios (FTZ, HS Code Controlled, De Minimis) is a separate,
gated next step, pending confirmation.

Not yet committed or pushed — pending review and commit-message request, per the established
workflow.
