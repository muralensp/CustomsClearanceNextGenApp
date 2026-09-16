# ADR-0028: Rules Engine - Admin API (Minimal) (Day 34)

**Status:** Accepted
**Date:** 2026-09-16
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 7 (Days 31-35, Rules Engine Skeleton), Day 34 - fourth day of the non-parallelizable
build chain. Task: "Expose minimal API to create/update/deactivate a rule (no UI yet - Postman/
Swagger only)." Deliverable: "CRUD API for rules, testable via Swagger."

Days 31-33 built the schema, evaluation pipeline, and persisted decision trace. This is the first
day that gives a compliance admin (not just this session's own diagnostic endpoints) a real way to
change what the engine does - the entire reason ADR-0002 chose a configuration-driven engine over
hardcoded logic in the first place.

Two decisions confirmed with the user before building:
1. **Require the Admin role** on every endpoint (`PlatformPolicies.RequireAdmin`, the same policy
   `/diagnostics/secure` already uses) - this changes real compliance policy, not a throwaway
   proof, so it gets the same gate as every other consequential action in this codebase.
2. **A separate "add new version" endpoint**, not an in-place `PUT` overwrite of the current
   version's conditions/outcome - matches the effective-dated design ADR-0013/ADR-0025 already
   committed to: old evaluations and `DecisionTrace` rows keep referencing the unchanged old
   `RuleVersion`, and a new version can be scheduled with its own `EffectiveFrom`.

## Decision

1. **`src/Api/RulesAdmin/RuleAdminEndpoints.cs`** - a new `MapGroup("/api/rules")`, all endpoints
   gated by `RequireAdmin`: `POST /` (create a Rule + its first RuleVersion + Conditions in one
   call), `GET /` (list, summary projection), `GET /{id}` (detail, full version/condition graph),
   `PUT /{id}` (update only the Rule's own mutable fields - Name/Description/Priority; RuleCode/
   Category are identity, never editable), `POST /{id}/deactivate` (soft-deactivation, per
   ADR-0025 - never a hard delete), `POST /{id}/versions` (add a new RuleVersion).
2. **Two of ADR-0013/ADR-0025's explicitly-deferred invariants, enforced here as promised**:
   RuleCode uniqueness (checked before insert, returning a real `DomainValidationException`/422,
   not a raw 500 from a database constraint violation) and no two RuleVersions of the same Rule may
   have overlapping effective-date windows.
3. **`RuleVersionEffectiveDateValidator`** (`Platform.RulesEngine`) - the overlap-checking logic,
   extracted as pure logic rather than left inline in the endpoint, so it is covered by that
   project's existing 95% core-tier coverage gate (ADR-0007) rather than proven only by live
   testing. Implements the **supersede exception** found via live testing and confirmed with the
   user: adding a version after an existing **open-ended** version (`EffectiveTo == null`) is not
   an overlap to reject, it is the normal way a policy gets updated going forward - the prior
   version's `EffectiveTo` is auto-closed to the day before the new version starts. This does not
   rewrite history: a `DecisionTrace` row already recorded still references the same
   `RuleVersionId` with its original outcome/conditions; only the window narrows for evaluations
   dated after the close.
4. **All detail/list responses are projected to named DTOs**, not raw entities - same discipline
   Day 31's diagnostic endpoint already established (`Rule`/`RuleVersion`/`RuleCondition`'s
   bidirectional navigation properties throw a JSON serialization cycle if returned directly).

### Two real bugs found and fixed during live verification

1. **Enum values sent as strings from a real client failed to bind.** The Day-Wise Plan's own
   deliverable wording - "testable via Postman/Swagger" - means a human typing `"DeMinimis"` into a
   request body, not an enum's raw integer value. `System.Text.Json`'s default enum converter
   expects a number unless a `JsonStringEnumConverter` is registered; the first live `POST
   /api/rules` call threw a raw `BadHttpRequestException`. Fixed by registering
   `JsonStringEnumConverter` globally via `ConfigureHttpJsonOptions`.
2. **A malformed request collapsed to a generic 500, not a 400.** `ExceptionHandlingMiddleware`
   only special-cased `DomainValidationException`; any other exception - including Minimal API's
   own `BadHttpRequestException` for bad JSON/model binding - fell through to the "unexpected
   error"/500 branch, hiding a client-fixable mistake behind a response that looked like a server
   bug. Fixed with a new `ErrorMapper` branch mapping `BadHttpRequestException` to a real 400, with
   a matching Warning (not Error) log level, and unit tests for both the mapper and the full
   middleware pipeline.

## Verification

Live, end-to-end, real JWT-authenticated calls against a running container (not just unit tests):
no-token request → 401; create → 201 with the full nested response; duplicate `RuleCode` → 422
(`BR-RULE-001`); get/list → 200; update → 200 with only the mutable fields changed; add-version
with a genuinely conflicting window → 422 (`BR-RULE-002`); add-version superseding an open-ended
version → 201, with the prior version's `EffectiveTo` correctly auto-closed; deactivate → 200 with
`active: false`; **a subsequent real evaluation via Day 32's pipeline confirmed the deactivated
rule no longer fires** - closing the loop across Days 31-34's whole build chain, not just proving
this day's endpoints in isolation.

`RuleVersionEffectiveDateValidator`: 8 new unit tests (no existing versions, supersede case,
before-an-open-ended-version is a real conflict not a supersede, overlapping/non-overlapping/
adjacent/boundary-inclusive closed-version cases, multiple existing versions). `ErrorMapper`/
`ExceptionHandlingMiddleware`: 2 new tests for the `BadHttpRequestException` fix. 61 new/changed
test assertions total across the two projects; `Platform.RulesEngine` at 95%+ line coverage
maintained, `Platform.ErrorHandling` improved to 100% line coverage. Full-solution `dotnet build`/
`dotnet format --verify-no-changes`/`dotnet test`, all clean with unpiped exit codes.

### Recurring RabbitMQ finding, this time actually root-caused and fixed at the infrastructure level

The familiar `.erlang.cookie: eacces` crash recurred multiple times this day, including against a
**named** volume (switched from anonymous, on the theory that `postgres-dev-data`'s named volume
had never hit this - that alone did not fix it). Diagnosed further: a plain `docker start` retry
against the *same, already-existing* volume (no recreate) succeeded immediately every time,
proving this is a one-time race at brand-new-volume first boot on this machine's Docker Desktop/
WSL2 setup (the image's entrypoint chown of `.erlang.cookie` occasionally loses a race), not
corrupted or stray state. Fixed with two complementary changes: `restart: on-failure:5` on the
`rabbitmq` service (so Compose retries automatically without manual intervention), and
`build/scripts/smoke-test.sh` wraps its own `docker compose up -d --build` call in a 3-attempt
retry loop, since `up`'s dependency-wait can give up before the container's own restart completes.
Verified clean across 6 consecutive fresh `docker compose down -v` / smoke-test cycles after the
fix - the first genuinely reliable resolution of this recurring class of issue (Days 26/27/30/33
each found a different-looking but ultimately related cause and applied a one-off fix; this is the
first fix built to self-heal automatically).

## Consequences

- Day 35 (Rules Engine Review) can now load 2-3 real India-specific sample rules through this
  actual admin API, not by hand-seeding the database - proving the admin API and the evaluation
  pipeline work together as a compliance admin would actually use them.
- `RuleVersionEffectiveDateValidator`'s supersede behavior is now a documented, tested part of the
  effective-dating model - any future admin UI (Phase 3+) built on top of this API inherits the
  same "adding a version narrows the prior open-ended version's window" behavior automatically.
- The `BadHttpRequestException` -> 400 fix benefits every endpoint in the `Api` host, not just
  `/api/rules` - a real cross-cutting bug fix surfaced by this day's specific testing.
- `restart: on-failure` plus a retrying CI script is a more defensible permanent fix than the
  session's prior per-day workarounds (pruning volumes, re-pulling images) - flagged as the
  pattern to reach for first if this exact symptom class recurs on other services in the future.

## Related

[[ADR-0025]] (Day 31 - the schema this API authors), [[ADR-0026]] (Day 32 - the evaluation
pipeline this API's deactivate/version changes are proven against), [[ADR-0027]] (Day 33 -
DecisionTrace, unaffected by superseding a RuleVersion), [[ADR-0013]] (the invariants this day
enforces, exactly as scoped there), [[ADR-0011]] (Security & Audit - RequireAdmin gating),
Day-Wise Plan Days 31-35 (Rules Engine Skeleton, non-parallelizable build chain).
