# BOSS NextGen — Customs Clearance Platform (India MVP)

Enterprise Customs Brokerage Management Platform. India is the first jurisdiction (ICEGATE / ITC-HS).

Solo-developed by the project owner with Claude (Anthropic) as design/build support — see `docs/` for the full trail of decisions.

## Where to Start

All PRD, phase-level/day-level plans, and Phase -1 deliverables now live in one place: `Phase-1_Architecture_Engineering_Prep/`. Highlights:

- **Product Requirements**: `Phase-1_Architecture_Engineering_Prep/CustomsClearance_PRD.docx`
- **Delivery Plan**: `Phase-1_Architecture_Engineering_Prep/CustomsClearance_Phasewise_DevlopmentPlan_v2.docx` (phase-level), `Phase-1_Architecture_Engineering_Prep/CustomsClearance_India_DayWise_Plan_Phase-1_and_Phase0.docx` (day-level)
- **Domain Model**: `Phase-1_Architecture_Engineering_Prep/Day1_Bounded_Context_Map_v2.docx` through `Day4_ER_Diagram_v1.0.docx`, plus `Multimodal_Shipment_Journey_Integration_ER_v1.1.docx`, all in that same folder
- **Business Process**: `Phase-1_Architecture_Engineering_Prep/Business_Process_Details.md`
- **Architecture Decisions**: `adr/` — 0001 stack, 0002 rules engine, 0003 repo/branching, 0004 security/audit, 0005 CI platform, 0006 Clean Architecture adaptation, 0007 static analysis/coverage, 0008 CD pipeline/environments, 0009 test automation, and 0010-0035 onward (platform libraries, Phase 0 services, Api host modules) — see the `adr/` folder for the full list
- **Quality Gate Checklists**: `docs/quality-gates/` — Gate 1 (Architecture) through Gate 5 (Release), used at every phase exit and referenced from the PR template
- **API Contracts**: `openapi/` (REST), `asyncapi/` (events, populated as each context's events are implemented), `postman/` (contract tests via Newman)
- **Dev Environment**: `docker-compose.yml` (Gateway, Api, PostgreSQL, RabbitMQ, Mailpit, Loki, Promtail, Prometheus, Tempo, Grafana) — see `adr/0008` and `adr/0015`; per-worker isolated stacks via `build/scripts/agent-env.sh` and `docs/PARALLEL-WORK.md`
- **Work Items**: `docs/WORK-ITEMS.md`

## Stack

ASP.NET Core (C#) + PostgreSQL, EF Core, portability-preserved for a future SQL Server migration. See `adr/0001-technology-stack.md`.

## Repository Structure

Monorepo, Modular Monolith, organized bounded-context-first (see `adr/0003-repository-branching-delivery-strategy.md`):

```
src/
  Platform/              Shared libraries: Common, Logging, ErrorHandling, Security, Audit, AuditPersistence,
                         Persistence, Messaging, Observability, RulesEngine, Configuration, Notifications,
                         ReferenceData (Eventing, Workflow still scaffolding)
  Gateway/               YARP API gateway, the only published edge
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
  Api/                   Host; each capability is an IApiModule in Api/Modules (see docs/PARALLEL-WORK.md)
tests/
  Unit/ Integration/ Contract/ Performance/ Security/
```

## Contributing (Solo Workflow)

See `CONTRIBUTING.md`. Short version: trunk-based development, short-lived `feature/*` branches, Conventional Commits, PR-to-self before merging to `main` — even solo.
