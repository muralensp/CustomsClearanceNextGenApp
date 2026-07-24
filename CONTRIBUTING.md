# Contributing Guide

This project is developed solo (one human, one AI pairing partner). This guide still applies in full — it exists precisely so the discipline holds up if/when the team grows, and so every change stays traceable for audit purposes (this is a regulatory platform). See `adr/0003-repository-branching-delivery-strategy.md` for the full rationale.

## Branching

- `main` is always deployable and protected. No direct commits.
- `release/*` branches only when preparing a production release.
- No long-lived `develop`/`staging` branches.

### Branch naming

```
feature/{CONTEXT-CODE}-{seq}-{short-slug}
bugfix/{CONTEXT-CODE}-{seq}-{short-slug}
hotfix/{CONTEXT-CODE}-{seq}-{short-slug}
refactor/{short-slug}
spike/{short-slug}
docs/{short-slug}
test/{short-slug}
```

Examples: `feature/SHIP-001-shipment-creation`, `feature/DECL-003-boe-validation`, `spike/rules-engine-prototype`.

Context codes (see `docs/WORK-ITEMS.md` for the running log of work items per context):

| Code | Context |
|---|---|
| SHIP | Shipment Management (incl. Journey/Leg/Tracking/Manifest-Association/Milestone) |
| CSHIP | Courier Shipment Management |
| MAN | Manifest Management |
| SEG | Segregation Engine |
| COM | Commodity Management |
| HSC | HS Classification Engine |
| DUTY | Duty & Tax Engine |
| PER | Permit Management |
| CMP | Compliance Engine |
| DECL | Declaration Management |
| PARTY | Party / Customer Management |
| WF | Workflow Engine |
| CGW | Customs Gateway Integration |
| ADM | User / Org Administration |
| DOC | Document Management |
| FIN | Financial / Charges |
| RPT | Reporting & Analytics |
| PLAT | Platform (Common, Security, Audit, Eventing, RulesEngine, Notifications) |

## Commits

[Conventional Commits](https://www.conventionalcommits.org/):

```
feat(shipment): add shipment creation API
fix(declaration): handle ICEGATE timeout
refactor(rule-engine): simplify evaluator
docs(prd): update chapter 7
test(duty): add GST calculation tests
perf(search): optimize HS lookup
build(ci): add code coverage
chore(deps): upgrade EF Core
```

## Pull Requests

Every change goes through a PR, including solo PR-to-self — never merge directly to `main`. Use the template in `.github/pull_request_template.md`. This creates a permanent record of design intent, review evidence, and requirement linkage, which matters for this platform's audit/traceability requirements (PRD Ch.6.11, 7.14, 8.14) independent of team size.

## Definition of Done

A feature is complete only when:

- Requirement implemented and traceable to a PRD chapter / Day-Wise Plan day / ADR / work item
- Unit tests passing (>=95% coverage for core business/rules logic, >=80% for CRUD-heavy master data, per the Phase-wise Plan v2)
- Integration tests passing
- EF Core migration included and reviewed (rollback considered) — not Flyway, per `adr/0001` and `adr/0003`
- OpenAPI updated (if REST contract changed)
- AsyncAPI updated (if event contract changed)
- Documentation updated
- ADR updated or added (if an architectural decision changed)
- Decision Trace verified (for any Rules Engine change, per `adr/0002`)
- Audit logging verified
- PR approved (self-approved with recorded checklist, for solo work) and merged

## Releases

Semantic Versioning: `vMAJOR.MINOR.PATCH`, aligned with the API/event versioning philosophy in `Day6_API_Standards_v1.1.docx`.
