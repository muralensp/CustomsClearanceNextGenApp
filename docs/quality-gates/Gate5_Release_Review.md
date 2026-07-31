# Gate 5 — Release Review

Run at: promotion between environments (dev → staging → prod, per `adr/0008-cd-pipeline-and-environment-promotion.md`) — not per bounded context, per phase.

## SIT (System Integration Testing)

- [ ] All bounded contexts involved in the release pass their individual Gate 1–3 checklists
- [ ] Cross-context integration verified end-to-end (e.g. Shipment → Segregation → Declaration → Customs Submission, per the scenario walkthroughs in `Day4_Scenarios_Revalidation_against_v1.1.docx`)
- [ ] `bash build/scripts/smoke-test.sh` (containerized dev environment) and `bash build/scripts/contract-test.sh` (Postman/Newman) both pass, per Days 13–14

## UAT (User Acceptance Testing)

- [ ] Business stakeholder (the Product Owner, per `project_setup` memory — solo team) has reviewed and accepted the release's functional scope against the relevant PRD chapter(s)
- [ ] Any open questions logged during domain-model review (Days 1–4) that are in scope for this release are explicitly resolved, not silently carried forward

## Regression

- [ ] Full CI pipeline green on the release candidate: Build, Lint, Unit Tests + Coverage Gate, Integration Tests, Containerize + Smoke Test, API Contract Tests (all jobs from `.github/workflows/ci.yml`, per Days 11–14)
- [ ] No previously-passing test disabled or skipped to make this release green, without a tracked work item explaining why (`docs/WORK-ITEMS.md`)

## Load Test

- [ ] Performance validated against the PRD's stated targets (Chapter 1.3: 500,000 shipments/day; Chapter 3.5 SLA targets per stage) for any release touching a high-volume path (Shipment, Declaration, Commodity, Segregation)
- [ ] No new N+1 query pattern or missing index identified under load (ties back to Gate 1's performance item, re-verified under realistic volume here)

## DR Test (Disaster Recovery)

- [ ] Database backup/restore procedure verified for this release's schema state (EF Core migrations, per `adr/0001`)
- [ ] Rollback plan documented and, where feasible, rehearsed — per the Definition of Done in `CONTRIBUTING.md`

## Security Test

- [ ] SAST (SonarAnalyzer, per `adr/0007`) and dependency/SCA scan clean, or any findings explicitly accepted with a documented reason
- [ ] DAST and/or penetration testing completed for releases reaching Production, per `Day10_Security_Audit_Standards_v1.0.docx`, Section 16 — required before Phase 8 (Customs Gateway/ICEGATE integration) certification specifically, and good practice for any Production release before then
- [ ] Manual approval recorded for any Production promotion — never automatic, per `adr/0008`'s environment promotion policy, given this is a regulatory customs platform

---

**Reviewed by:** _______  **Date:** _______  **Scope (context/PR/release):** _______  **Result:** Pass / Pass with noted exceptions / Fail
