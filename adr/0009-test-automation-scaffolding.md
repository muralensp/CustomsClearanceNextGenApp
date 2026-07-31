# ADR-0009: Test Automation Scaffolding — Three Isolated Layers

**Status:** Accepted
**Date:** 2026-07-31
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Day 14 of the Day-Wise Plan calls for a unit test framework, an integration test framework, and Postman/Newman (or equivalent) for API contract tests, with a deliverable of "test scaffolding with one passing sample test per layer." The unit layer (xUnit + coverlet, Day 11/12) already existed; this ADR covers adding the other two layers and, critically, keeping all three isolated from each other so a slow or environment-dependent test in one layer never blocks or dilutes the fast feedback loop of another.

## Decision

Three distinct test layers, each with its own portable CI script (per ADR-0005's discipline), each verified locally against a real passing sample before being wired into CI:

| Layer | Framework | Scope | Script | Coverage Gate? |
|---|---|---|---|---|
| Unit | xUnit + coverlet.msbuild | `tests/Unit/**` only | `build/scripts/test.sh` | Yes (95%/80% per ADR-0007) |
| Integration | xUnit + `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`) | `tests/Integration/**` only, in-process (no docker) | `build/scripts/integration-test.sh` | No — not a unit-coverage policy target |
| API Contract | Postman collection run via Newman (`npx newman`) | Black-box, over real HTTP, against the docker-compose dev environment (ADR-0008) | `build/scripts/contract-test.sh` | No — proves contract compliance, not code coverage |

## Why Three Separate Scripts, Not One

Originally `test.sh` ran `dotnet test` across the whole solution with no project filter. Adding the integration test project to the same solution meant a bare `dotnet test` would silently sweep integration tests into the unit+coverage job. This was caught and fixed before it became a real problem: `test.sh` now explicitly iterates `tests/Unit/*/*.csproj` only, and a new `integration-test.sh` iterates `tests/Integration/*/*.csproj` only. This keeps:

- Coverage thresholds (a unit-test-specific policy per ADR-0007) measured only against unit tests, not diluted or invalidated by integration test projects that don't declare coverlet properties at all.
- The fast unit+coverage feedback loop fast — integration tests (in-process HTTP pipeline, slower than pure unit tests) run as their own CI job in parallel, not serially appended to the unit job.
- Room for the integration layer to grow real infrastructure dependencies later (a real Postgres test container, for example) without that dependency ever leaking into the unit test job's requirements.

## Why the Contract Test Layer Is Separate From Both

The integration test layer (`WebApplicationFactory`) exercises the Api's HTTP pipeline in-process — fast, no docker required, but it never leaves the .NET test host. The contract test layer, by contrast, runs against the actual containerized dev environment from Day 13 (`docker compose up`), hitting the API purely over the network exactly as an external caller or the OpenAPI/AsyncAPI contracts (Day 7) would. These test different things: integration tests prove the application's internal wiring; contract tests prove the deployed artifact behaves correctly from outside, matching its published contract. Collapsing them into one layer would have hidden this distinction.

## Local Verification Before Wiring Into CI

| Check | Result |
|---|---|
| `dotnet test tests/Integration/Api.IntegrationTests/...` (before wiring into a script) | 1/1 passed |
| `bash build/scripts/test.sh` (unit, re-scoped) | 100% coverage, threshold satisfied, integration project correctly excluded |
| `bash build/scripts/integration-test.sh` (new script) | 1/1 passed, unit project correctly excluded |
| `bash build/scripts/contract-test.sh` (new script, full run including `npx newman` download) | Dev environment built and started, health check passed after 1 attempt, all 3 Postman assertions passed, JUnit report generated, clean teardown |

Consistent with every prior day: no CI step was wired around a command that hadn't already been proven to work locally first.

## Consequences

- Every future bounded context that gets a real implementation should add: a unit test project under `tests/Unit/{Context}.Tests/` (with coverage properties declared per ADR-0007's classification table), and, where it exposes HTTP endpoints, either an integration test under `tests/Integration/` or new Postman requests added to `postman/CustomsClearance.postman_collection.json` (or both, depending on what's being verified).
- The Postman collection currently has exactly one request (`/health`). It is explicitly a scaffold, not a real contract test suite — it must grow alongside the actual OpenAPI-defined endpoints (`openapi/shipment.yaml`, `openapi/declaration.yaml`, Day 7) as those are implemented, not left as a permanent placeholder.
- `contract-test.sh` depends on Node.js/npm being available on the CI runner (true by default for GitHub Actions' `ubuntu-latest`) — if the Azure DevOps migration (ADR-0005) ever moves to a different agent image, this dependency should be re-verified, not assumed.
- `npx newman` downloads the package fresh on first use in an environment without a warm npm cache; this was observed to take longer than the rest of the pipeline's steps combined on a cold run. Revisit with an explicit `npm ci`/cached install step if this becomes a meaningful CI time cost once more real contract tests are added.

## Related

[[ADR-0005]] (CI platform strategy — all three new/changed scripts kept in lockstep across `ci.yml` and `azure-pipelines.yml`). [[ADR-0007]] (coverage gate policy — why integration/contract layers are deliberately excluded from it). [[ADR-0008]] (the docker-compose dev environment the contract test layer runs against). Day 7 (OpenAPI stubs the Postman collection should grow to mirror).
