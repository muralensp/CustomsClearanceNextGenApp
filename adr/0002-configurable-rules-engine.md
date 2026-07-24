# ADR-0002: Configurable, Effective-Dated Rules Engine (Config-Driven, Not Code-Driven)

**Status:** Accepted
**Date:** 2026-07-24
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

The PRD's architectural note (Ch.6, following Declaration Management) explicitly recommends a configuration-driven platform over a code-driven one: business rules like "HS Code controlled → CT = FML" should be evaluated from a configurable rule repository, not hardcoded in application logic, so compliance administrators can update customs policy without a software release.

This was reinforced as a **binding design constraint** during Day 1 domain scoping (Day1_Bounded_Context_Map_v2, Section 4.4): India's de minimis treatment has no single universal threshold and depends on import channel, purpose, importer type, and commodity — any hardcoded value would be wrong within one CBIC notification cycle. The Day 3 data model (Rule / RuleVersion / RuleCondition / DecisionTrace, ER Diagram v0.2) already implements this as an entity model. ADR-0002 formalizes the architectural decision that data model assumed, before Phase 0 (Days 31–35, Rules Engine Skeleton) begins implementation.

## Decision

Build a **custom, in-house configurable rules engine** in C# (per ADR-0001's stack), rather than adopting a third-party rules-engine library (e.g., Drools, NRules) or hardcoding segregation/compliance/duty-applicability logic directly in service classes.

The engine evaluates `Rule` → `RuleVersion` (effective-dated) → `RuleCondition` (AND-only per version, per Day 3/Day 4 resolution) against a `Facts` object assembled per evaluation context (Shipment, CourierShipment, Commodity, Declaration), producing an `Outcome` plus a `DecisionTrace` for explainability, ordered by `Priority` per the conflict-resolution table in PRD Ch.5.9.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Third-party rules engine (e.g. NRules for .NET) | Mature RETE-algorithm implementation, less code to write | Adds a dependency with its own learning curve; RETE's forward-chaining model is more powerful than this platform needs (our rules are simple condition/priority/outcome evaluations, not complex multi-fact inference chains) | Overkill for the actual complexity of segregation/compliance rules as scoped; adds risk for a solo developer to debug a third-party engine's internals |
| Hardcoded business logic (if/else in service classes) | Fastest to write initially | Violates the PRD's explicit architectural principle; cannot support effective-dating (a CBIC notification changing a threshold would require a code release); cannot produce a DecisionTrace without significant additional bespoke code anyway | Directly contradicts the Day 1 binding constraint (Section 4.4) and the PRD's own recommendation |
| Custom-built engine (chosen) | Exactly matches the Rule/RuleVersion/RuleCondition/DecisionTrace model already designed (Day 3); no third-party dependency risk; full control over effective-dating and explainability | More code to write than adopting a library | Best fit — the actual requirement (AND-only conditions, priority ordering, effective-dating, decision trace) is narrow enough that a custom evaluator is straightforward, and it exactly matches the schema already agreed |

## Consequences

- Phase 0, Days 31–35 (Rules Engine Skeleton) will implement: the storage schema (Day 31), a custom evaluation pipeline (Day 32) that loads applicable `RuleVersion`s (by `EffectiveFrom`/`EffectiveTo` matching the evaluation date), matches `RuleCondition`s against the assembled facts, and orders by `Priority` (Day 33 adds trace capture, Day 34 adds the admin CRUD API).
- The Segregation Engine, Compliance Engine, and any duty-applicability logic in the Duty & Tax Engine all consume the **same** rules engine rather than each building bespoke conditional logic — directly reusing the shared infrastructure noted in Day 1 (Section 4.1 ownership boundaries).
- OR logic across conditions is deliberately not supported at the schema level for MVP (Day 3/Day 4 resolution): OR scenarios are handled by authoring multiple `Rule`s at the same `Priority` within the same `RuleCategory`. This keeps the evaluator simple; revisit only if Phase 3 rule authoring (real India rules) proves this insufficient.
- Because this is custom-built, any future requirement for more sophisticated inference (e.g. rules that reference the output of other rules mid-evaluation, true RETE-style forward chaining) would require either extending this engine significantly or replacing it with a third-party library — flagged here as the explicit trigger condition for revisiting this ADR.

## Related

[[ADR-0001]] (stack choice — engine must be C#-implementable). Day 3 ER Diagram v0.2 (Section 5, Rule/RuleVersion/RuleCondition/DecisionTrace entities). Day 4 ER Diagram v1.0 (Section 4.4/4.5, resolution of OR-logic and decision-trace-granularity open questions). Day-Wise Plan Days 19 (design spike) and 31–35 (Phase 0 build).
