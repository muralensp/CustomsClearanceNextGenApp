# ADR-0004: Security & Audit Standards

**Status:** Accepted
**Date:** 2026-07-25
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Day 10 of the Day-Wise Plan calls for auth approach, field-level audit logging pattern, and data-at-rest/in-transit encryption approach. The user requested a broader scope covering 17 specific areas plus a gap analysis. This ADR records the binding decisions; the full Day 10 deliverable document carries the detailed rationale and tables for each area.

This platform carries an unusually high security/audit bar even for an enterprise system: it processes government filings (ICEGATE), personal data of individual importers (Day 4 Scenario B — gift imports with only PAN/no GSTIN), financial data (duty/GST/Cess), and is subject to the PRD's repeated audit-immutability requirements (Ch.6.11, 7.14, 8.14) plus India's Digital Personal Data Protection Act, 2023 (DPDPA) — the last of which was not explicitly named in the PRD or prior days and is treated here as a gap closed.

## Decision

Adopt the standards detailed in `Day10_Security_Audit_Standards_v1.0.docx`. Binding summary:

| Area | Decision |
|---|---|
| Authentication | OAuth2 / OpenID Connect via the Identity service (Phase 0 Day 23); JWT bearer tokens; no local password accounts for MVP (internal users only, via the org's IdP) |
| Authorization | Policy-based (ASP.NET Core), role claims mapped to Day 1 personas (Broker, Supervisor, Compliance Officer, Admin) + field-level checks in the Application layer, not the API layer |
| API Security | Rate limiting, input validation at the edge, OWASP API Top 10 checklist applied per endpoint, mandatory on all external-facing paths (per API Standards v1.0/v1.1) |
| Service-to-Service Auth | mTLS or signed service tokens (not user JWTs) for internal service-to-service calls (Day 6 v1.1 — these bypass the gateway but still must authenticate) |
| Encryption | TLS 1.2+ in transit everywhere; AES-256 at rest for the database and any file storage; secrets in a managed vault, never in config files or source |
| Data Classification | Public / Internal / Confidential / Highly Confidential, mapped explicitly to ER v1.1 entities/fields |
| Audit Logging | A single AuditEvent model, append-only, reused across all contexts — not one audit mechanism per context |
| Field-Level Audit | Old value / new value capture on every classified Confidential+ field change, keyed by CorrelationId |
| Audit Storage | Append-only table(s) in PostgreSQL for MVP, partitioned by month; retention per India customs record-keeping rules (5 years minimum, confirmed as an open item) |
| Correlation & Traceability | Reuses the CorrelationId established in API Standards v1.0 Section 7 / v1.1 Section 3.1 — one mechanism, not a separate audit-specific ID |
| Logging Standards | Structured (JSON) logs, no sensitive/Confidential+ data in log bodies, correlation ID on every line |
| Data Masking | Masking rules by classification tier, applied at the API/read-model layer, not by mutating stored data |
| Digital Documents | Digital signature required for ICEGATE submission payloads per PRD Ch.3 Stage 10; separate from generic "Document Management" (Day 1 context #13) |
| Security Headers | Standard OWASP secure header set (HSTS, CSP, X-Content-Type-Options, etc.) enforced at the API Gateway |
| Password Policy | Deferred — no local accounts planned for MVP; policy defined now anyway as a documented fallback, not implemented until/unless local accounts are introduced |
| Security Testing | SAST + dependency/SCA scanning in CI (Phase 0 Days 11-15); DAST and formal penetration testing scheduled pre-Phase-8 (before ICEGATE certification) |
| Compliance | Indian IT Act 2000 + rules, India DPDPA 2023 (new — not previously named), CBIC/Customs record-keeping requirements, PRD's own audit requirements |
| Business Audit vs Technical Audit | Explicitly separated: business audit = who-did-what-to-which-declaration (already modeled via DecisionTrace, Day 3, and Declaration versioning, Day 2/6); technical audit = system-level security events (login, permission change, config change) |

### Gaps identified beyond the supplied list

- **Secrets management & rotation** (distinct from "encryption of secrets at rest") — key/secret lifecycle, rotation cadence, and access control for the vault itself.
- **Data retention & purge policy** — the PRD mandates immutable audit history but never states a retention *duration*; India customs/tax record-keeping rules impose one, and this was undecided until now.
- **India DPDPA 2023 compliance** — personal data of individual importers (Day 4 Scenario B) makes this platform a "Data Fiduciary" under Indian law; not named anywhere in the PRD or prior days.
- **Non-repudiation for ICEGATE submissions** — the PRD (Ch.3 Stage 10) mentions digital signing but this had not been connected to a concrete standard until now.
- **Rate limiting / DoS protection** — implicit in "API Security" but easy to under-scope; called out explicitly.
- **Dependency/SCA scanning** — distinct from generic "Security Testing," called out as its own CI gate.

## Consequences

- Every ER v1.1 entity/field must be tagged with a classification (Section 6 of the Day 10 deliverable) before Phase 0 schema implementation — this is now a required step in Days 31+ (or earlier, wherever entities are first implemented), not an afterthought.
- The Audit Event model becomes a Platform/Audit shared library (src/Platform/Audit, per ADR-0003's structure), consumed by every context — consistent with the "shared infrastructure, not per-context reinvention" pattern already used for the Rules Engine (ADR-0002).
- Retention duration and DPDPA-specific consent/data-subject-rights handling are flagged as open items requiring your confirmation (Section 17 of the Day 10 deliverable) before Phase 3 (where individual-importer personal data is first actually processed at scale).

## Related

[[ADR-0001]] (stack — encryption/TLS implementation is ASP.NET Core + PostgreSQL native). [[ADR-0002]] (DecisionTrace already provides part of the business-audit trail). [[ADR-0003]] (Platform/Audit as a shared module). Day 6 API Standards v1.0/v1.1 (CorrelationId, error format). Day 1 personas (role mapping). Day 4 ER Diagram v1.0 (Party PAN/GSTIN/IEC fields subject to classification).
