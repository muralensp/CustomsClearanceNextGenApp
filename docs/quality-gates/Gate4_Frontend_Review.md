# Gate 4 — Frontend Review

Run at: completion of a bounded context's frontend layer (Day-Wise Plan's "Layer 4" per phase). **Not yet applicable** — per the Phase-wise Plan v2 and the Day-Wise Plan, UI development is intentionally deferred until Phase 10 (Customer Experience), after backend/API layers across contexts have stabilized. This checklist is formalized now (Day 15), ahead of use, so it exists before the first frontend work starts rather than being improvised at that point — the same "decide the mechanism before you need it" approach used for CI (Day 11), CD (Day 13), and test automation (Day 14).

No frontend framework has been chosen yet (that decision is out of scope for Phase -1). Items below are written to be framework-agnostic; the specific framework decision, once made, should get its own ADR and may add framework-specific items to this checklist.

## UX

- [ ] UI consumes only stable, versioned APIs (per `Day6_API_Standards_v1.0.docx`) — no direct database access, no bypassing the API layer
- [ ] User-facing error messages are derived from the API's RFC 7807 `ProblemDetails` response, not raw exception text
- [ ] Role-based UI behavior matches the Day 1 Permissions Matrix (a Broker should not see Admin-only actions, etc.)

## Accessibility

- [ ] Meets a stated accessibility standard (e.g. WCAG 2.1 AA) — to be confirmed as an explicit target when frontend work begins, not assumed
- [ ] Keyboard navigability and screen-reader compatibility checked for any new screen

## Performance

- [ ] Initial load and key interaction performance measured against a stated target (to be defined alongside the frontend framework decision) — not left unmeasured
- [ ] Large list views (Shipment/Declaration search, per PRD FR-004) use the API's pagination envelope (`Day6_API_Standards_v1.0.docx`, Section 4) rather than fetching unbounded result sets client-side

## Responsive Design

- [ ] UI is usable on both desktop and tablet viewports at minimum (mobile support scope to be confirmed with the eventual frontend decision)
- [ ] No horizontal scrolling or clipped content on supported viewport sizes for core workflows

---

**Reviewed by:** _______  **Date:** _______  **Scope (context/PR/release):** _______  **Result:** Pass / Pass with noted exceptions / Fail / Not Applicable (pre-Phase-10)
