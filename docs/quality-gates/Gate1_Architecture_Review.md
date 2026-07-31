# Gate 1 — Architecture Review

Run at: the start of implementing any new bounded context module, and at Phase -1 exit (Day 20).

## Standards Followed

- [ ] Module is placed under the correct `src/{Context}/` folder, matching the bounded-context list in `adr/0003-repository-branching-delivery-strategy.md` (17 top-level contexts + Shipment Management sub-contexts)
- [ ] Project split follows `adr/0006-clean-architecture-adaptation.md`: `Context.csproj` (Domain+Application, no EF Core reference) + `Context.Infrastructure.csproj` (EF Core, Postgres-specific config) — Infrastructure references Context, never the reverse
- [ ] No direct `ProjectReference` between two different bounded contexts (cross-context communication is events/read-models or an explicitly justified direct call, per `Day5_Context_Map_and_Domain_Event_Catalog_v1.1.docx`, Section 4/5) — flagged here until architecture tests (`adr/0006`'s tracked follow-up) automate this check
- [ ] If the module introduces a new segregation/compliance/duty-applicability rule, it uses the shared Rules Engine (`adr/0002-configurable-rules-engine.md`), not bespoke conditional logic

## Naming Conventions

- [ ] C# naming follows `Day8_Coding_Standards_v1.0.docx`, Section 2 (PascalCase types/methods, `I`-prefixed interfaces, `_camelCase` private fields, `{Entity}Id` for GUID primary keys matching ER Diagram v1.1 naming)
- [ ] `.editorconfig` naming-convention warnings are clean (`dotnet format --verify-no-changes --severity warn`, per Day 12)
- [ ] Branch/commit naming follows `CONTRIBUTING.md` (`feature/{CONTEXT-CODE}-{seq}-{slug}`, Conventional Commits) and the work item is logged in `docs/WORK-ITEMS.md`

## Security

- [ ] Any new Confidential/Highly Confidential field (per the classification table in `Day10_Security_Audit_Standards_v1.0.docx`, Section 6) is identified and its masking/logging treatment (Sections 11–12 of that document) is applied
- [ ] Any new cross-context or service-to-service call follows the transport rules in `Day6_API_Standards_v1.1.docx`, Section 2 (gateway for external ingress only; internal calls authenticated per `adr/0004`, Item 4)
- [ ] No secrets, connection strings, or credentials committed to source (checked against `.gitignore` and the Day 10 secrets-management standard)

## Performance

- [ ] New data access goes through EF Core, no N+1 query patterns introduced (explicit `.Include()`, no lazy loading, per Day 8 Coding Standards Section 4)
- [ ] Any new endpoint intended for high-volume paths (Shipment/Declaration/Commodity, per the PRD's 500,000 shipments/day target) has been considered against the SLA targets in the PRD (Chapter 3.5)

## Documentation

- [ ] A new ADR is added (or an existing one updated) if this module represents an architectural decision not already covered — per the "living document" principle established since ADR-0001
- [ ] `README.md`'s pointers are updated if this module changes what a newcomer needs to know to navigate the repo
- [ ] Domain events this module publishes/consumes are reflected in the Day 5 event catalog (or its eventual `asyncapi/` successor, per `adr/0003`)

---

**Reviewed by:** _______  **Date:** _______  **Scope (context/PR/release):** _______  **Result:** Pass / Pass with noted exceptions / Fail
