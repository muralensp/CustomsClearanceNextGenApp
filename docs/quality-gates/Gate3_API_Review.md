# Gate 3 — API Review

Run at: completion of a bounded context's API layer (Day-Wise Plan's "Layer 3" per phase), before that context's frontend consumption begins (Layer 4) or before the API is considered externally callable.

## Swagger / OpenAPI

- [ ] An OpenAPI stub/spec exists under `openapi/{context}.yaml`, following the pattern established in Day 7 (`openapi/shipment.yaml`, `openapi/declaration.yaml`)
- [ ] The implemented endpoints match the OpenAPI spec exactly (no undocumented endpoints, no spec-only endpoints that don't exist) — verified by the API Contract test layer (`adr/0009`, `postman/CustomsClearance.postman_collection.json`) actually exercising them
- [ ] Swagger UI (via `Swashbuckle.AspNetCore`, already wired in `src/Api/Program.cs`) reflects the current contract in the Development environment

## API Contracts

- [ ] Response envelope matches `Day6_API_Standards_v1.0.docx`: pagination envelope for list endpoints (Section 4), RFC 7807 `ProblemDetails` error format with `errors[].code` mapped to a PRD Business Rule ID (Section 6), `correlationId` present on every response (Section 7)
- [ ] Money fields use `decimal` with an explicit currency code field, never a bare number (Section 9)
- [ ] Dates/times are ISO 8601 UTC (Section 9)

## Versioning

- [ ] URL path versioning (`/api/v{n}/...`) used, per Section 3 of `Day6_API_Standards_v1.0.docx`
- [ ] Any breaking change bumps the major version; additive-only changes do not, per the same section
- [ ] If this endpoint publishes or consumes domain events, the event envelope includes `eventVersion` per the unified versioning philosophy in `Day6_API_Standards_v1.1.docx`, Section 3

## Authentication

- [ ] Endpoint enforces the correct role/policy per the Day 1 Permissions Matrix (Broker/Supervisor/Compliance Officer/Admin) via ASP.NET Core policy-based authorization, not ad-hoc role checks (Day 8 Section 8, `adr/0004` Item 2)
- [ ] Field-level restrictions (e.g. read-only fields post-submission, per PRD FR-003) are enforced in the Application layer, not just assumed at the API layer
- [ ] If this is a service-to-service call (not user-facing), it follows the Day 6 v1.1 transport rules (no API Gateway for internal traffic; authenticated via signed service tokens or mTLS per `adr/0004` Item 4)

## Integration Tests

- [ ] An integration test exists under `tests/Integration/{Context}.IntegrationTests/` using `WebApplicationFactory`, per the pattern in `adr/0009-test-automation-scaffolding.md`, covering at minimum the happy path and one failure/validation path
- [ ] `bash build/scripts/integration-test.sh` passes locally
- [ ] If this endpoint is part of the externally-callable contract, at least one Postman request/assertion exists in `postman/CustomsClearance.postman_collection.json` and `bash build/scripts/contract-test.sh` passes

---

**Reviewed by:** _______  **Date:** _______  **Scope (context/PR/release):** _______  **Result:** Pass / Pass with noted exceptions / Fail
