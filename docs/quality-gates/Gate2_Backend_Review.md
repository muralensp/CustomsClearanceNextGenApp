# Gate 2 — Backend Review

Run at: completion of a bounded context's Domain/Application/Infrastructure layers (Day-Wise Plan's "Layer 2" per phase), before that context's API layer begins.

## Business Rules

- [ ] Every PRD-referenced business rule (BR-xxx IDs) relevant to this context is implemented and traceable — the implementing code or its `DomainValidationException` carries the matching `BusinessRuleCode` (per `Day8_Coding_Standards_v1.0.docx`, Section 4)
- [ ] Domain entities use private setters with explicit state-transition methods (e.g. `Declaration.Submit()`), not raw property assignment, per Day 8 Section 4
- [ ] Any segregation/compliance/duty-applicability logic is expressed as Rules Engine rules (`adr/0002`), not hardcoded conditionals

## Unit Tests

- [ ] A unit test project exists at `tests/Unit/{Context}.Tests/`, declaring `CollectCoverage`/`Threshold`/`ThresholdType`/`ThresholdStat` directly in its `.csproj`, per `adr/0007-static-analysis-and-coverage-gates.md`
- [ ] Domain/Application tests use in-memory fakes for repositories — no real database connection in a unit test (per Day 8 Section 5 and `adr/0009`'s layer separation)
- [ ] Test naming follows `MethodName_Scenario_ExpectedBehavior` (Day 8, Section 5)

## Code Coverage

- [ ] Threshold met: **95%** line coverage for core business/rules logic (Segregation, Duty & Tax, Declaration lifecycle, Rules Engine, Compliance), **80%** for CRUD-heavy master data / shared platform libraries — per the classification table in `adr/0007`
- [ ] `bash build/scripts/test.sh` passes locally (exits 0) before pushing — a failing threshold fails the build, it does not just warn

## SonarQube (SonarAnalyzer.CSharp)

- [ ] `dotnet build` introduces no new SonarAnalyzer warnings without a documented reason (per `CONTRIBUTING.md`'s Definition of Done) — check the Build job log, not just local output, since analyzer versions can differ
- [ ] Any suppressed/ignored Sonar rule has an inline comment explaining why, not a blanket `#pragma warning disable` at file scope

## Security Scan

- [ ] No new Highly Confidential field (GSTIN/IEC/PAN/DGFT license refs, per `Day10_Security_Audit_Standards_v1.0.docx`, Section 6) is logged, returned unmasked by default, or stored unencrypted, per Sections 5/11/12 of that document
- [ ] Any new audit-worthy action (state change, override, permission grant) produces an `AuditEvent` with the correct `EventCategory` (BusinessAudit vs. TechnicalAudit, per Section 7/17.1)
- [ ] Dependency/SCA scan (per `adr/0004`, Item 16 and `adr/0007`'s CI integration point) shows no new high/critical vulnerabilities introduced by this change's package references

---

**Reviewed by:** _______  **Date:** _______  **Scope (context/PR/release):** _______  **Result:** Pass / Pass with noted exceptions / Fail
