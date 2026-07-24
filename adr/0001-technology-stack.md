# ADR-0001: Technology Stack — ASP.NET Core (C#) + PostgreSQL

**Status:** Accepted
**Date:** 2026-07-22
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

The Day-Wise Plan (Day 8) calls for a formal stack decision before any database or API implementation begins, per the "architecture-first, backend-first" delivery approach (Phase-wise Development Plan v2). The decision was actually made conversationally ahead of Day 8, once industry-comparable platforms were reviewed:

- CargoWise (market-leading customs brokerage platform) is built on .NET/C#.
- SAP GTS and Oracle GTM are Java-based, tied to their respective enterprise ecosystems.
- The platform's functional and non-functional requirements (PRD Ch.1.3: 500,000 shipments/day, 99.95% uptime, sub-second tariff lookups, immutable audit trails, versioned/effective-dated rules) are satisfiable by either a mature JVM stack or a mature .NET stack — there is no requirement that forces one over the other.
- The team is a single implementer (the user) working with AI pairing (Claude), on Windows with PowerShell as the primary shell.

## Decision

Adopt **ASP.NET Core (C#)** for all backend services, and **PostgreSQL** as the initial relational database, with an explicit, binding requirement that the codebase remain compatible with a later migration to **SQL Server** if needed.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Java (Spring Boot) | Larger rules-engine ecosystem (Drools); common in India government-facing integrations | Less natural fit for solo Windows-based development velocity | No functional requirement favors it strongly enough to outweigh the velocity cost for a solo developer |
| Node.js / TypeScript (full-stack) | Fast iteration, single language front-to-back | Weaker fit for heavy financial/decimal-precision calculation (duty/tax) and less proven at this category's enterprise scale | Not an industry-standard choice for this specific application category; higher risk for the Duty & Tax Engine's precision requirements |
| .NET / C# (chosen) | Proven at this exact problem (CargoWise); strong tooling on Windows; first-class `decimal` type for financial calculations; mature EF Core ORM | Smaller open-source rules-engine ecosystem than Java | Best fit for solo velocity + proven industry precedent |

## Database Portability Requirement (binding constraint)

Because PostgreSQL is the starting database but SQL Server migration must remain possible without a rewrite, all data access must follow these rules:

1. **EF Core is the only data-access layer.** No raw ADO.NET/Dapper SQL in business logic.
2. **No PostgreSQL-specific SQL features** in application code: no `JSONB` operators, no array column types, no `ON CONFLICT` upsert syntax, no Postgres-only functions. Where a Postgres-specific capability seems necessary, isolate it behind a repository interface with a documented fallback for SQL Server.
3. **No stored procedures for business logic**, consistent with the Day-Wise Plan's Layer 1 guidance ("stored procedures only where justified") — stored procs are the least portable part of a schema between the two engines.
4. **EF Core migrations** should avoid provider-specific migration builder calls where a provider-agnostic equivalent exists.

## Consequences

- Every entity from ER Diagram v1.1 (Shipment, Declaration, Commodity, Party, Rule/RuleVersion/RuleCondition, Manifest, ShipmentJourney, TransportLeg, etc.) will be modeled as EF Core entities with Fluent API configuration, not database-first stored-procedure-driven design.
- The rules engine (Day 0/Phase 0 skeleton, Day 19 design spike) must be implementable in C# — ruling out JVM-only rules libraries (e.g. Drools) unless a .NET-compatible equivalent is used, or a custom rules evaluator is built (the current plan already assumes a custom-built evaluator per Day 3's Rule/RuleVersion/RuleCondition model, so this is not a new constraint).
- Money/decimal handling (API Standards v1.0, Section 9) is naturally well-supported via C#'s native `decimal` type end-to-end with PostgreSQL's `numeric` column type.
- If SQL Server migration is later required, the primary risk areas to re-test are: EF Core migration replay, any full-text search usage (Postgres and SQL Server implement this differently), and connection-resiliency/retry configuration (provider-specific).

## Related

Confirmed in conversation prior to Day 8; recorded here per the Day-Wise Plan's Day 8 requirement. See Day6_API_Standards_v1.0.docx Section 9 (money/decimal handling) and the project memory file `tech_stack.md`.
