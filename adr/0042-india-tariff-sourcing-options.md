# ADR-0042: India Tariff Data Sourcing - Options (No Decision Yet)

**Status:** Proposed
**Date:** 2026-10-19
**Deciders:** Product Owner (decision pending), Claude (Day 45, item 4 research)

## Context

Day 39 (ADR-0033) loaded placeholder reference data and deferred the India tariff master (ITC-HS, duty rates) to
Phase 1, with sourcing and licensing open. Day 45 item 4 asked for web research on where that data can legitimately
come from. Full note: `docs/research/INDIA-TARIFF-SOURCING-OPTIONS.md`.

## Decision

None yet. The note recommends evaluating a hybrid (official CBIC/DGFT documents for the code list and ITC-HS status,
a commercial feed or the ICEGATE calculator only to cross-check rates), after getting vendor quotes and redistribution
terms. The Product Owner must decide and must confirm licensing.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Extract official PDFs ourselves | Authoritative, no licence fee | Recurring manual effort, error-prone | Not decided |
| Commercial content subscription | Maintained, low engineering | Cost, lock-in, redistribution limits | Not decided |
| Hybrid | Authoritative base plus cross-check | Two sources can disagree | Recommended to evaluate |
| Scrape ICEGATE / third-party sites | Cheap | Terms unknown, no authority, fragile | Not recommended |

## Consequences

- The tariff master needs effective dating on every row and a repeatable load process (nomenclature changes with the
  Finance Act, rates by notification at any time, and a large HS re-mapping is expected around 2028).
- Findings come from web search only; official sites could not be opened and no licence was read in full. Every
  *(unverified)* item in the note must be confirmed before reliance.
- Revisit when the Product Owner answers the open questions in section 5 of the note.

## Related

[[ADR-0002]], [[ADR-0033]], `docs/research/INDIA-TARIFF-SOURCING-OPTIONS.md`.
