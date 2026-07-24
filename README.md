# BOSS NextGen — Customs Clearance Platform (India MVP)

Enterprise Customs Brokerage Management Platform. India is the first jurisdiction (ICEGATE / ITC-HS).

Solo-developed by the project owner with Claude (Anthropic) as design/build support — see `docs/` for the full trail of decisions.

## Where to Start

All PRD, phase-level/day-level plans, and Phase -1 deliverables now live in one place: `Phase-1_Architecture_Engineering_Prep/`. Highlights:

- **Product Requirements**: `Phase-1_Architecture_Engineering_Prep/CustomsClearance_PRD.docx`
- **Delivery Plan**: `Phase-1_Architecture_Engineering_Prep/CustomsClearance_Phasewise_DevlopmentPlan_v2.docx` (phase-level), `Phase-1_Architecture_Engineering_Prep/CustomsClearance_India_DayWise_Plan_Phase-1_and_Phase0.docx` (day-level)
- **Domain Model**: `Phase-1_Architecture_Engineering_Prep/Day1_Bounded_Context_Map_v2.docx` through `Day4_ER_Diagram_v1.0.docx`, plus `Multimodal_Shipment_Journey_Integration_ER_v1.1.docx`, all in that same folder
- **Architecture Decisions**: `adr/` — start with `adr/0001-technology-stack.md`, `adr/0002-configurable-rules-engine.md`, `adr/0003-repository-branching-delivery-strategy.md`, `adr/0004-security-audit-standards.md`
- **API Contracts**: `openapi/` (REST), `asyncapi/` (events, populated as each context's events are implemented)
- **Work Items**: `docs/WORK-ITEMS.md`

## Stack

ASP.NET Core (C#) + PostgreSQL, EF Core, portability-preserved for a future SQL Server migration. See `adr/0001-technology-stack.md`.

## Repository Structure

Monorepo, Modular Monolith, organized bounded-context-first (see `adr/0003-repository-branching-delivery-strategy.md`):

```
src/
  Platform/              Shared: Common, Security, Audit, Eventing, Workflow, RulesEngine, Notifications
  Administration/
  PartyManagement/
  ShipmentManagement/    incl. Journey, TransportLeg, Tracking, ManifestAssociation, DeliveryMilestone sub-modules
  CourierShipmentManagement/
  ManifestManagement/
  Segregation/
  Commodity/
  Classification/
  DutyTax/
  Permit/
  Compliance/
  Declaration/
  CustomsGatewayIntegration/
  WorkflowEngine/
  DocumentManagement/
  FinancialCharges/
  Reporting/
  Api/
tests/
  Unit/ Integration/ Contract/ Performance/ Security/
```

## Contributing (Solo Workflow)

See `CONTRIBUTING.md`. Short version: trunk-based development, short-lived `feature/*` branches, Conventional Commits, PR-to-self before merging to `main` — even solo.
