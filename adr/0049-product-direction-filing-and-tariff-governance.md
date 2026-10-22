# ADR-0049: Product Direction (Filing With ICEGATE) and Tariff Data Governance

**Status:** Accepted for direction and governance; sourcing remains with ADR-0042 (Proposed)
**Date:** 2026-10-22
**Deciders:** Product Owner (accepted each recommendation in the Phase 1 kickoff interview), Claude (design support)

## Context

ADR-0042 records tariff-sourcing options without a decision and lists open questions: does the platform file with
ICEGATE or only advise, what budget, who owns `IsVerified` sign-off. A web search in the kickoff interview found that
software vendors do file directly with ICEGATE (for example MIC-CUST) and that filers need an ICEGATE registration and
a Class 3 digital signature (sources disagreed on whether the signature is mandatory). It found nothing on how
CargoWise, Cleartax, Logisys or Cybersoft connect, and no current ICES technical specification. Statements about
broker systems preparing declarations in-product and transmitting to ICES are general knowledge, unverified.

## Decision

1. **Direction: file with ICEGATE eventually; advise-only first.** Phase 1 and early phases ship classification and
   duty/GST calculation as advisory output, marked unverified until the data is signed off.
2. **Action added to Phase 1:** obtain the official ICEGATE/ICES integration specification and onboarding rules
   (empanelment, sandbox access, signing rules). Until then the target authority stays "ICEGATE, unconfirmed".
3. **`IsVerified` is a platform-level act.** The Product Owner signs off at first. `IsVerified` records **who verified
   and in what capacity** (for example `PO-verified` versus `professionally-reviewed`). **Live ICEGATE filing stays
   disabled for any tariff row that has only had PO verification.** Each load carries a named human signature
   recorded in the audit trail.
4. **Sourcing:** official CBIC/DGFT documents as the base. A paid feed is adopted only if a quote fits an evaluation
   ceiling of about **INR 1,00,000 per year** (a ballpark; commercial feeds probably cost more, unverified, no quotes
   seen). Collect vendor quotes and redistribution terms in parallel. FTA/preferential rates are out of Phase 1
   unless a pilot customer needs them. Jurisdiction: India only.
5. **Scope of the Phase 1 sourcing workstream** (original plan section 2.3): a decision document for three data sets,
   ITC-HS tariff, exchange rates and lookup tables. The loader and real ingestion are Phase 2 (Day 88 of the day-wise plan).
6. Effective dating on every tariff row, a repeatable load process, and awareness that HS 2028 forces a re-mapping
   (from ADR-0042) carry over.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Permanently advisory | Smaller scope, no ICEGATE build | Weaker category; customer re-keys declarations | Direction is a brokerage platform |
| External licensed reviewer from day one | Defensible control | Cost | Gate on live filing instead |
| Paid feed from day one | Maintained data | Cost; licence terms unknown | Official base plus quotes |

## Consequences

- Filing cannot be enabled until a professional review exists for the rows involved; this is a deliberate brake.
- The tariff master is a Phase 1 workstream of research/procurement, not engineering, until quotes arrive.
- Revisit when the ICES specification is obtained or when a vendor quote fits the ceiling.

## Related

ADR-0033, ADR-0042; `docs/research/INDIA-TARIFF-SOURCING-OPTIONS.md`; checklist 7.12.

Sources used in the interview (secondary, unverified):
[MIC-CUST ICEGATE filing](https://mic-cust.com/landingpages/global-implementations/solutions/customs-clearance-solution-for-india),
[ICEGATE overview](https://referencer.in/General_Information/ICEGATE.aspx),
[ICEGATE digital signature](https://www.certificate.digital/icegate/).
