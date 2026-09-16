# Day 34 Execution Log — Rules Engine: Admin API (Minimal)

Phase 0, Week 7 (Days 31-35, Rules Engine Skeleton), Day 34 of the non-parallelizable build chain.
Deliverable: "CRUD API for rules, testable via Swagger."

---

## Step 1 — Two decisions confirmed before building

Asked the user directly: (1) should `/api/rules/*` require Admin authorization, matching the
existing `/diagnostics/secure` policy? Confirmed **yes** - this is a real capability, not a
diagnostic. (2) should changing a rule's conditions/outcome go through a separate "add new
version" endpoint, or should `PUT` overwrite the current version in place? Confirmed **separate
endpoint**, matching the effective-dated design already committed to.

## Step 2 — Build the real admin API

`src/Api/RulesAdmin/` - `RuleAdminEndpoints.cs` (create/list/get/update/deactivate/add-version, all
`RequireAdmin`-gated) and `RuleAdminModels.cs` (request/response DTOs). Enforces the two invariants
ADR-0013/ADR-0025 explicitly deferred to this day: RuleCode uniqueness and no overlapping
RuleVersion effective-date windows.

## Step 3 — First live test surfaced two real bugs

`POST /api/rules` with `"category": "DeMinimis"` returned a raw 500. Root causes, both fixed:
1. `System.Text.Json`'s default enum converter rejects string values without a
   `JsonStringEnumConverter` registered - fixed via `ConfigureHttpJsonOptions`. Real: the Day-Wise
   Plan's own deliverable wording ("testable via Postman/Swagger") means a human typing a string
   into a request body, not an enum's integer value.
2. The underlying `BadHttpRequestException` (a malformed request, the client's mistake) fell
   through `ExceptionHandlingMiddleware`'s generic "unexpected error"/500 branch instead of a real
   400 - fixed with a new `ErrorMapper` branch, plus a matching Warning log level and unit tests
   for both `ErrorMapper` and the full middleware pipeline (this fix benefits every endpoint in the
   Api host, not just `/api/rules`).

## Step 4 — Second live test surfaced a real workflow gap

Retried after both fixes - create/get/list/update/duplicate-rejection all worked. But adding a new
RuleVersion after the first (open-ended) one was rejected as an "overlap," since an open-ended
window extends indefinitely by definition - correct per the literal invariant, but it meant there
was no way to ever supersede a rule's current policy. Asked the user: auto-close the prior
open-ended version, or require an explicit separate "close version" call first? Confirmed
**auto-close** - the standard effective-dating pattern, and doesn't rewrite history since past
evaluations/DecisionTrace rows are unaffected. Implemented, and extracted the overlap/supersede
logic into `RuleVersionEffectiveDateValidator` (`Platform.RulesEngine`) rather than leaving it
inline in the endpoint, so it's covered by that project's 95% coverage gate - 8 new unit tests
covering the supersede case, the "before an open-ended version is a real conflict, not a
supersede" case, and standard overlap/adjacency/boundary cases.

## Step 5 — Full live verification, closing the loop across the whole week

Real JWT-authenticated calls end to end: no-token → 401; create → 201; duplicate RuleCode → 422;
get/list → 200; update → 200 (mutable fields only); genuinely-conflicting version → 422;
superseding version → 201 with the prior version's EffectiveTo correctly auto-closed; deactivate →
200 (`active: false`). **Then ran a real evaluation through Day 32's pipeline and confirmed the
deactivated rule no longer fired** - proving Days 31-34 work together as a real compliance admin
would actually use them, not just each day's own isolated proof.

## Step 6 — RabbitMQ recurrence, this time actually root-caused and fixed for good

The familiar startup crash recurred again, including against a named volume (tried first, didn't
fix it alone). Diagnosed further: a plain `docker start` retry against the *same already-created*
volume succeeded immediately every time - proving a one-time first-boot race, not corrupted/stray
state. Fixed with two complementary, self-healing changes: `restart: on-failure:5` on the
`rabbitmq` service, and `build/scripts/smoke-test.sh` wraps its own `docker compose up` in a
3-attempt retry loop (since `up`'s dependency-wait can give up before the container's restart
completes). Verified clean across 6 consecutive fresh cycles - the first fix in this recurring
saga (Days 26/27/30/33) built to self-heal automatically rather than requiring another manual
prune/re-pull each time it resurfaces.

## Step 7 — Final verification

Full-solution `dotnet build`/`dotnet format --verify-no-changes`/`dotnet test` (9 test projects,
61 new/changed assertions across `RuleVersionEffectiveDateValidatorTests`/`ErrorMapperTests`/
`ExceptionHandlingMiddlewareTests`), all clean with unpiped exit codes. `build/scripts/
smoke-test.sh` (the actual CI script) re-run 3 times from fresh state, all clean.

## Outcome

**Deliverable met, with two real bugs and one real workflow gap found and fixed via live testing,
not assumed from a clean build**: a real, authorization-gated CRUD API for rules, proven end to
end including its effect on the evaluation pipeline itself. **Decision recorded**:
`adr/0028-rules-engine-admin-api.md` (Accepted).

**Status**: Complete. Per the Day-Wise Plan's strict build chain, **Day 35 — Rules Engine Review**
(load 2-3 real India-specific sample rules through this admin API, verify evaluation + trace
output) is next, pending explicit instruction to begin.
