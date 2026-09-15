# ADR-0014: AI/ML Integration Strategy

**Status:** Proposed
**Date:** 2026-09-15
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

The user asked whether a set of Digicust-style AI capabilities could be integrated into BOSS
NextGen, and whether now (Day 19 of Phase -1) is the right time. The capability list supplied:

1. **Document Intake & Intelligent Processing** — multi-channel extraction (email/PDF/Excel/EDI),
   table/layout normalization, AI email inbox (thread parsing, intent classification, dossier
   assignment, draft responses).
2. **AI Customs Declarations & Data Matching** — automated field pre-fill, trade document code
   assignment (N380/U116/Y271-style), cross-system invoice-to-master-data matching.
3. **Compliance, Risk & Export Control** — export control/sanctioned-entity/dual-use screening,
   preferential origin cross-checks against FTAs (EUR.1, A.TR, GSP), live EORI/A.TR/T1-MRN
   validation.
4. **Tariff Classification & Master Data Management** — automated HS code classification, SKU
   description rewriting into standardized trade language, catalog deduplication.
5. **Audits, Analytics & Specialized Modules** — AI audit replay against historical data, BTI
   application construction, authorization/quota tracking against stock movements.

This is a strategy decision, not an implementation — no AI feature code is being built by this
ADR. It exists to record the integration shape and sequencing *before* any of Phase 0's bounded
contexts are built out, the same way ADR-0013 recorded the Rules Engine's design ahead of its Days
31-35 build.

Current codebase state as of Day 19 (verified by direct inspection, not assumption):
- All 17 `src/` bounded-context folders (`Administration`, `Classification`, `Commodity`,
  `Compliance`, `CourierShipmentManagement`, `CustomsGatewayIntegration`, `Declaration`,
  `DocumentManagement`, `DutyTax`, `FinancialCharges`, `ManifestManagement`, `PartyManagement`,
  `Permit`, `Reporting`, `Segregation`, `ShipmentManagement`, `WorkflowEngine`) are empty
  `.gitkeep` placeholders — no business logic exists in any of them yet, `DocumentManagement`
  included.
- `src/Platform` has real, working ports: `IRepository<TEntity, TId>` (Persistence),
  `IEventPublisher`/`IEventHandler<T>`/`DomainEventEnvelope<T>` (Messaging), `IAuditWriter`
  (Audit) — see ADR-0012.
- The `IDeclarationRepository`-over-`IRepository` narrower-port pattern that ADR-0012 describes is
  documented intent only; no bounded context has exercised it in code yet.
- ADR-0013's five-interface Rules Engine pipeline (`IFactsProvider`, `IRuleSetLoader`,
  `IConditionEvaluator`, `IPriorityResolver`, `IDecisionTraceRecorder`) exists only as a
  synchronous throwaway spike at `spike/RulesEngineSpike/`, not wired into `CustomsClearance.sln`
  or implemented against real persistence. The real build is scoped to Phase 0 Days 31-35.
- No hosting/cloud provider decision has been made yet (ADR-0008 explicitly defers this to Day 21).
- ADR-0004 (Security & Audit Standards) already names DPDPA 2023 as a binding constraint given
  personal data (e.g. gift-import PAN-only records) and financial data (duty/GST/Cess) in scope.

## Decision

Adopt two integration shapes for AI features, both of which the existing architecture already
anticipates structurally, and sequence adoption against the Day-Wise Plan rather than against the
capability list's own internal order.

### Integration shape 1 — New bounded context

Applies to **Document Intake** (group 1) and **Catalog Deduplication** (part of group 4). These
have no dependency on the Rules Engine: Document Intake is a pure ingestion concern
(`DocumentManagement` context, currently empty and ready to be designed correctly rather than
retrofitted), and Catalog Dedup is a `Commodity`/master-data batch concern. Both can be scoped as
ordinary bounded-context work following the same Clean Architecture adaptation as every other
context (ADR-0006): `Context.csproj` (Domain+Application merged) plus `Context.Infrastructure.csproj`,
narrower ports built on top of `Platform.Persistence`/`Platform.Messaging` per the
`IDeclarationRepository`-over-`IRepository` pattern ADR-0012 already names.

### Integration shape 2 — Pluggable adapter feeding `IFactsProvider`

Applies to **Compliance/Risk/Export Control** (group 3), **HS Code Classification** and
**description rewriting** (group 4), and eventually **Declarations & Matching** (group 2), which
consumes the outputs of the other two. The AI component (export-control screening, HS code
suggestion, preferential-origin cross-check) sits as a fact-enrichment or suggestion source that
feeds `IFactsProvider`, behind a narrow port (e.g. an `IHsCodeSuggestionService`-shaped interface),
proposing a value plus confidence — never as a replacement for `IConditionEvaluator`,
`IPriorityResolver`, or `IDecisionTraceRecorder`. Those three stay deterministic and rule-based.

This is a hard rule, not a preference: ICEGATE government filings require the actual clearance
decision to remain fully explainable via `DecisionTrace` (ADR-0002, PRD Ch.5.13), and DPDPA 2023
compliance (ADR-0004) is easier to reason about when AI components are bounded to suggestion/
enrichment rather than final decision-making. **AI suggests; the Rules Engine decides and
records.**

### Sequencing

| Feature group | Shape | Earliest sound entry point |
|---|---|---|
| 1. Document Intake | New context | Independent of Rules Engine — can be scoped whenever prioritized |
| 4b. Catalog Dedup | New context (Commodity) | Independent of Rules Engine — can be scoped whenever prioritized |
| 4a. HS Code Classification, description rewriting | Adapter → `IFactsProvider` | After Phase 0 Days 31-35 (Rules Engine implemented, not just spiked) |
| 3. Compliance/Risk/Export Control | Adapter → `IFactsProvider` | After Phase 0 Days 31-35 |
| 2. Declarations & Matching | Consumer of 1 + 3/4 | After 1 and 3/4 are in place |
| 5. Audits/Analytics/BTI/Quota | Uses `IAuditWriter`/Persistence directly | After real transactional data exists in `Declaration`/`Compliance`/`Permit` — a data-availability gate, not a code-readiness one |

Building any group-2/3/4a adapter before Days 31-35 risks rework: the interface contract it plugs
into (`IFactsProvider`) is still a synchronous throwaway spike, not the real async contract ADR-0013
specifies. It would also front-load model-hosting and data-residency decisions that ADR-0008 has
deliberately deferred to Day 21.

### Explicitly out of scope for this ADR

- Selecting a specific AI/ML vendor, model, or hosting approach (API-based vs. self-hosted) — a
  Day 21 infra/CD decision input, not decided here.
- Designing `IHsCodeSuggestionService` or any other adapter's actual interface shape — deferred
  until the Rules Engine's real (non-spike) `IFactsProvider` contract exists.
- `DocumentManagement` context design (parsers, layout normalization, email inbox) — a full
  bounded-context scoping exercise in its own right, not a byproduct of this strategy ADR.
- Any code changes. This ADR is Proposed, recording strategy only.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Build AI adapters now, against the Day-19 spike's synchronous interfaces | Fastest path to a demo-able AI feature | Spike is explicitly throwaway (ADR-0013); real contract is async and not yet wired to persistence/DI — near-certain rework | Rejected: optimizes for a demo over the working agreement to build day-by-day against settled contracts |
| Let AI components implement `IConditionEvaluator` directly (AI decides, not just suggests) | Could shortcut adapter-layer design | Breaks DecisionTrace explainability required for ICEGATE audit; conflates a probabilistic suggestion with a deterministic, auditable decision | Rejected on compliance grounds (ADR-0004), not just architectural taste |
| Defer all five feature groups uniformly until Phase 0 Rules Engine is complete | Simple, one rule to remember | Ignores that Document Intake and Catalog Dedup have no Rules Engine dependency and could proceed independently, needlessly delaying low-risk work | Rejected: sequencing should follow actual dependencies, not a blanket "wait for everything" rule |

## Consequences

- This ADR does not unblock or authorize any AI feature work. Per CLAUDE.md's working agreements
  ("do not start the next day's work without explicit instruction"), building any of the five
  feature groups — including the two independent ones (Document Intake, Catalog Dedup) — requires
  separate explicit instruction.
- Day 21's infra/CD decision (ADR-0008) should fold in AI hosting and DPDPA 2023 data-residency
  considerations as an additional input, since the decision is already open at that point.
- Phase 0 Days 31-35 (Rules Engine implementation) become the natural checkpoint to revisit this
  ADR and design the first real adapter (most likely HS Code suggestion or export-control
  screening, per the sequencing table above), once `IFactsProvider`'s real async contract exists.
- No architecture rework is required to adopt this strategy later — every bounded context it
  touches is currently an empty placeholder, so there is nothing to retrofit.

## Related

[[ADR-0002]] (config-driven Rules Engine this ADR's adapter shape must not bypass), [[ADR-0004]]
(Security & Audit Standards — DPDPA 2023 constraint), [[ADR-0006]] (Clean Architecture adaptation
— per-context port pattern reused here), [[ADR-0008]] (CD/environments — hosting decision AI
integration should feed into at Day 21), [[ADR-0012]] (Persistence/Messaging platform libraries —
`IDeclarationRepository`-over-`IRepository` precedent), [[ADR-0013]] (Rules Engine design — the
`IFactsProvider` contract this ADR's adapters plug into), Day-Wise Plan Days 31-35 (Rules Engine
build) and Day 21 (infra/CD decision).
