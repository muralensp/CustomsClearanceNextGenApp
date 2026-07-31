# Business Process Details — BOSS NextGen Customs Brokerage Platform (India MVP)

This document describes the end-to-end business process the platform automates, in narrative form, for anyone (product, engineering, compliance, or a future team member) who needs to understand how a shipment actually moves through the system without reading the full PRD or every Day-Wise deliverable. It is derived entirely from this project's own artifacts — the PRD (Chapters 1–8), the Day 1–13 domain-model and architecture decisions, and the ADRs — not from any external reference document. Where a process step corresponds to a specific PRD chapter, bounded context, or ADR, that traceability is noted inline.

Jurisdiction: India (ICEGATE / ITC-HS, GST, Customs Duty, Cess). Stack: ASP.NET Core / PostgreSQL (ADR-0001).

---

## 1. Overview

The brokerage process consists of receiving shipment and manifest data, validating and classifying it, determining the correct customs treatment, calculating government charges, verifying permits and compliance, preparing and submitting a legal declaration to Indian Customs, tracking the declaration through to release, and maintaining a complete, audit-ready record of everything that happened along the way.

Every stage below is owned by one of the platform's bounded contexts (see `Day1_Bounded_Context_Map_v2.docx` and `Day4_ER_Diagram_v1.0.docx` for the full context map — 17 top-level contexts plus Shipment Management's Journey/Leg/Tracking sub-contexts). Contexts communicate primarily through domain events, with a small, explicitly justified set of direct (synchronous) calls at points where stale data would be legally or operationally unacceptable — see `Day5_Context_Map_and_Domain_Event_Catalog_v1.1.docx` and `Day6_API_Standards_v1.1.docx` for the full event-vs-direct-call rationale.

---

## 2. Receive Shipment and Manifest Information

A shipment enters the platform from an upstream logistics system (or, for courier shipments, from the courier's own system) as described in PRD Chapter 3, Stage 1 and Chapter 4 (Shipment Management). The Shipment Management context creates the shipment record, assigns a unique Shipment Number, and sets its status to `Downloaded` (FR-001).

Separately, and just as importantly, a carrier or authorized courier files a **manifest** — an Import General Manifest (IGM), Export General Manifest (EGM), or Courier Manifest — directly with Indian Customs. This is not optional paperwork: under Indian customs regulations, a formal Bill of Entry or a courier declaration cannot legally be lodged without referencing a valid manifest line. The platform's Manifest Management context (bounded context #17, added during the Day 4 domain-model review after this gap was identified — see `Day4_ER_Diagram_v1.0.docx`, Section 3) holds this manifest reference and validates it before any declaration can be submitted.

For **multimodal** shipments — for example, an ocean leg from origin to an Indian port followed by a road leg to the final destination — the platform does not treat "transport mode" as a single property of the shipment. Instead, each shipment has a **Journey** made up of one or more **Transport Legs** (Air, Ocean, Road, Rail), and the manifest is associated with the specific leg at which a customs boundary is actually crossed, not with the shipment as a whole. This distinction — adopted after a dedicated architectural review (`Multimodal_Shipment_Journey_Integration_ER_v1.1.docx`) — is what allows the platform to correctly handle a shipment that clears customs at one leg and then continues domestically without any further customs event.

**Courier shipments** are received and modeled as a distinct, first-class business flow (not a variant of ordinary cargo shipments), because Indian courier regulations impose a different manifest form, different declaration simplifications, and different de minimis treatment (see Section 4 below) than formal cargo. This was a deliberate domain decision, not an oversight — see `Day1_Bounded_Context_Map_v2.docx`, Section 4.3.

---

## 3. Validate the Shipment

Before any brokerage work begins, the platform validates that the shipment record is complete and internally consistent (PRD Chapter 4, FR-002): the shipment number is unique, origin and destination countries are valid, at least one commodity line exists, a shipper and consignee are present, and the declared currency is supported. Party-level identity fields — GSTIN, IEC (Importer-Exporter Code), and PAN — are checked according to the rules established for Indian filings; at least one of GSTIN or PAN is required for import/export/broker parties, except for personal gift shipments below the applicable value threshold, where an individual importer may legitimately have neither (see `Day4_Scenarios_Revalidation_against_v1.1.docx`, Scenario B).

If validation fails, the shipment is placed into a `Validation Error` status and a task is raised for a broker to resolve the issue — no shipment proceeds to segregation or declaration while validation errors remain outstanding.

---

## 4. Determine Customs Treatment (Segregation)

Once validated, every shipment is evaluated by the **Segregation Engine** (PRD Chapter 5) to determine its Clearance Type — Formal, Informal, Domestic, FTZ, De Minimis, and so on. This is one of the platform's most important design decisions, and it is entirely rule-driven rather than hardcoded: the engine loads a set of jurisdiction-specific, **effective-dated** rules and evaluates them against the shipment's facts (origin/destination, commodity classification, controlled-goods flags, permit requirements, customer profile, and — for courier shipments — import channel, import purpose, and importer type).

This effective-dating requirement exists because India's treatment of low-value or personal shipments is not a single fixed threshold; it depends on the combination of import channel, purpose, and importer type, and it can change through a CBIC notification without any change to the platform's code. A value alone is never allowed to determine the processing path (`Day1_Bounded_Context_Map_v2.docx`, Section 4.4 — carried through into the Rules & Segregation data model in `Day3_ER_Diagram_v0.2.docx` and formalized as an architectural decision in ADR-0002). Every segregation outcome produces a **Decision Trace** recording exactly which rules fired and why, so the outcome is fully explainable on demand, not just logged as a final answer.

A Supervisor may manually override an automated segregation decision, but only with a recorded reason, and the override itself becomes part of the auditable decision history rather than silently replacing it.

---

## 5. Create and Classify Commodities

Each shipment contains one or more commodities, derived from invoice information received with the shipment or entered by a broker (PRD Chapter 7). Commodity lines can be split (for example, where a single invoice line legitimately requires two different HS codes) or merged, subject to the constraints described in the Commodity Management module.

Each commodity is then classified against India's ITC-HS tariff (an 8-digit national extension of the 6-digit WCO Harmonized System) by the **HS Classification Engine** (PRD Chapter 8). Classification can be automatic — using a customer's prior approved classifications, historical classification patterns, or keyword matching — or manual, where a broker selects from a set of candidate codes ranked by confidence. Regardless of source, every classification is validated against the active, versioned tariff master before it is accepted, and every classification decision is recorded with its source and confidence score for audit. AI-assisted suggestions, where used, are advisory only and require broker confirmation unless a specific policy explicitly allows otherwise (PRD Ch.8, BR-HSC-006).

---

## 6. Calculate Duty, Tax, and Charges

With classification complete, the Duty & Tax Engine calculates the government charges applicable to each commodity: Basic Customs Duty, IGST, and Social Welfare Surcharge/Cess, using India's specific charge taxonomy rather than the generic "duty/excise/GST" language found in earlier general customs literature (`Day1_Bounded_Context_Map_v2.docx`, Section 4.2). Calculation follows a defined sequence — customs value, freight and insurance adjustments, statistical value, then duty, then IGST on the duty-inclusive value, then any applicable cess — and every calculated charge retains a reference back to the specific rule version that produced it, so a duty figure calculated last year can always be reproduced exactly as it was calculated then, even if tariff rates have since changed.

Brokerage fees (the platform's own service charges, as distinct from government charges) are calculated at the same stage but are tracked as a separate charge type, since they are not government-mandated and follow the platform's own fee schedule rather than a CBIC-published rate.

---

## 7. Verify Permits and Compliance

Certain commodities — controlled goods, items requiring a DGFT license, or goods subject to other government agency (OGA) approval — cannot proceed to declaration submission until the required permit is approved. The Permit Management context tracks each permit's status (Requested, Pending, Approved, Rejected, Expired) and blocks submission while any required permit remains unapproved.

Permit Management is designed as its own bounded context architecturally, but for the India MVP it is deliberately co-deployed with the Compliance Engine in the same service, since the two are tightly coupled in practice for controlled-goods handling; they remain logically separable for a future point at which they might need to scale or evolve independently (`Day1_Bounded_Context_Map_v2.docx`, Section 4.3). The Compliance Engine separately screens for restricted or prohibited goods, sanctioned parties, end-use restrictions, and any customer-level compliance flags, and a declaration cannot be marked ready for submission until compliance screening has explicitly cleared it.

Both the permit check and the compliance check at submission time are performed as direct (synchronous) calls rather than relying on the last known event, precisely because approving a declaration against stale permit or compliance data is not an acceptable risk on a regulatory platform (`Day5_Context_Map_and_Domain_Event_Catalog_v1.1.docx`, Section 5).

---

## 8. Prepare and Review the Declaration

Once segregation, classification, duty calculation, and permit/compliance checks are complete, the platform automatically creates a draft **Declaration** (PRD Chapter 6) — a Bill of Entry for imports, a Shipping Bill for exports, or a simplified courier declaration for courier shipments (modeled as a `DeclarationType` variant of the same Declaration entity, not a separate entity, per the resolution in `Day4_ER_Diagram_v1.0.docx`, Section 4.2). The declaration references the shipment (or courier shipment), the specific transport leg at which customs clearance applies, and the manifest line it corresponds to.

A broker reviews the draft declaration whenever manual attention is required — an HS code could not be determined automatically, a duty calculation needs adjustment, a required document is missing, or a compliance exception was raised. The broker can edit commodity details, upload supporting documents, add remarks, or request supervisor approval before resubmitting for validation. Every edit to a declaration creates a new, immutable version rather than overwriting the previous one, so the full history of how a declaration reached its final form is always available.

---

## 9. Submit to Customs and Process the Response

When all validations pass — mandatory fields complete, permits approved, compliance cleared, and a valid manifest reference confirmed — the declaration is electronically transmitted to Indian Customs through the Customs Gateway Integration context (targeting ICEGATE; the specific gateway/authority integration details remain pending final confirmation, tracked as an open item since Day 1). The submission payload is digitally signed for non-repudiation, and the submission timestamp and a correlation identifier are recorded so the transmission can always be traced.

Customs responds with one of several outcomes — Accepted, Rejected, Inspection Required, Permit Required, Additional Documents Requested, or a system error requiring retry — and the platform processes each of these asynchronously as the response arrives, updating the declaration's status and, where the response requires it, creating a broker task to resolve the issue (PRD Chapter 3, Stage 11).

---

## 10. Maintain Shipment and Declaration Status

Throughout this process, the platform maintains four synchronized status fields on every declaration — Clearance Type (CT), Rating Status (RAT), Customs Status (CUS), and Manifest Status (MAN) — under the control of the Workflow Engine. Manifest Status, in particular, is not an independent flag a user can set directly; it is derived from the linked Manifest record's own status and its match against the declaration, which is itself a correction made during the Day 4 domain review once the Manifest gap was identified.

Status transitions drive broker task assignment and escalation: a rejection creates a correction task, an inspection hold notifies the broker and pauses further processing, and an acceptance moves the shipment toward clearance. All of this is event-driven rather than polled — each status-changing event is published and consumed by the Workflow Engine and any other interested context, consistent with the platform-wide event-driven architecture principle established alongside the PRD's domain model.

---

## 11. Handle Amendments, Cancellations, and Resubmissions

If an error is discovered after submission — by the broker or by Customs — the platform supports an amendment workflow: the accepted declaration is cloned into a new editable version, changes are applied and duties/taxes recalculated where necessary, and the amendment is resubmitted while preserving a permanent link back to the original declaration. Cancellation is permitted only while the declaration has not yet been released and where Indian customs regulations allow it, and every cancellation request and its outcome is fully audited, consistent with the platform's broader audit requirements (see Section 13 below).

---

## 12. Clearance and Release

Once Customs approves the declaration, the platform records the clearance decision and release date, updates the shipment's status toward completion, and notifies any downstream logistics systems that the shipment is ready for onward movement or delivery — for a multimodal shipment, this may mean a domestic transport leg (with no further customs involvement) now begins.

---

## 13. Audit, Traceability, and Visibility

Every significant action in the process above — a status change, a manual override, a permit approval, a field edit on a Confidential or Highly Confidential piece of data (such as a party's GSTIN, IEC, or PAN) — is captured as an audit event, using a single shared audit model that distinguishes **business audit** (what happened to a specific shipment, commodity, or declaration, and why) from **technical audit** (system-level security events such as logins, permission changes, or configuration changes), per the security and audit standards defined for the platform. A single correlation identifier threads through the entire lifecycle of a shipment — its originating request, every internal service call, every published event, every audit record, and the eventual customs submission — so any shipment's full history can be reconstructed end-to-end for support, investigation, or regulatory audit purposes.

Shipment and declaration information is also made available for reporting and analytics purposes, and for external visibility to customers and downstream systems, at defined milestones throughout the lifecycle — though the full Reporting & Analytics capability is a later-phase concern, not part of the India MVP's core brokerage flow.

---

## 14. Retain and Archive Records

Consistent with Indian customs and tax record-keeping requirements, declaration and audit data is retained for a defined minimum period (a five-year baseline has been proposed pending explicit confirmation against current CBIC/GST regulation text — this figure should not be treated as final without that verification). Audit records are stored in an append-only structure that cannot be altered or deleted through normal application access, and historical tariff and rule versions are preserved indefinitely so that any past declaration can always be reproduced exactly as it was calculated and classified at the time, even after tariffs, exchange rates, or business rules have since changed.

---

## Traceability

This document consolidates process detail already established across the PRD (Chapters 1–8) and the Phase -1 Day-Wise deliverables (Days 1–13) into a single narrative reference, for anyone who needs the "how the business process actually works" view without assembling it from the individual day-by-day documents. It introduces no new process decisions of its own; where a statement here appears to go beyond what a prior document established, that prior document's title is cited inline above, and any genuine gap should be raised for a follow-up review rather than assumed settled by this document alone.
