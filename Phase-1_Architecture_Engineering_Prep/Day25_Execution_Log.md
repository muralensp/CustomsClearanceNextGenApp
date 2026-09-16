# Day 25 Execution Log — Base Security Framework

Phase 0, Week 5 (Core Infrastructure), Day 25 of the Day-Wise Plan. Deliverable: "Auth-protected
sample endpoint working."

---

## Step 1 — Confirm scope before building: does this reverse ADR-0016?

Day 25's plan wording ("wire security-lib into gateway") reads as extending ADR-0016's Day 22
decision (gateway is pass-through-only, no auth) rather than just re-proving it. Asked the user
directly rather than assuming either way. Confirmed: **add real JWT validation at the gateway**
(defense-in-depth) - the gateway becomes a second, independent enforcement layer using the same
`Platform.Security` policies the `Api` host already uses, not a replacement for the `Api` host's
own checks. Recorded as `adr/0019-base-security-framework.md`.

## Step 2 — Wire Platform.Security into the gateway

- `src/Gateway/CustomsClearance.Gateway.csproj` - added `ProjectReference` to `Platform.Security`.
- `src/Gateway/Program.cs` - added `AddCustomsClearanceAuthentication`/
  `AddCustomsClearanceAuthorization` + `UseAuthentication`/`UseAuthorization`, same calls the `Api`
  host makes.
- `src/Gateway/appsettings.json` - added the same `"Jwt"` section (key/issuer/audience) as the
  `Api` host's `appsettings.Development.json`, so a token valid at one is valid at the other.
- `src/Gateway/Dockerfile` - added the `Platform.Security` project copy steps (build + publish
  stages) the new `ProjectReference` requires.

## Step 3 — Per-route enforcement via YARP's native config, not custom code

Split the gateway's single catch-all route into two: `diagnostics-secure-route` (`Match.Path:
/diagnostics/secure`, `AuthorizationPolicy: RequireAdmin`) ahead of the existing catch-all
`api-route` (no policy). YARP's `RouteConfig.AuthorizationPolicy` is the documented, idiomatic
mechanism for this - no bespoke path-matching middleware needed. ASP.NET Core's endpoint routing
inherently prefers the more specific literal path over `{**catch-all}`, so no explicit `Order` was
needed for the two routes to resolve correctly.

Scoped to the **one** existing protected sample endpoint (matching the plan's own singular
wording), not a blanket "gateway requires auth for everything" policy - that would have blocked
`/auth/login` itself (no token exists yet to obtain a token) and every future public endpoint
without an ever-growing exception list. Opt-in per protected route, not opt-out from a global
requirement.

## Step 4 — Recurring bug: Day 24's RabbitMQ startup race, tighter margin this time

First live verification run crashed the `Api` at startup with the same
`BrokerUnreachableException` Day 24 already found and fixed - the existing 10-attempt/2s retry
budget (20s) was occasionally insufficient for RabbitMQ's actual startup time. Increased to 20
attempts (40s budget) in `RabbitMqConnectionHelper`. Re-verified across two consecutive fresh
`docker compose down -v` / `up --build` cycles with no further failures. Not a new bug - a
tightened margin on an already-fixed one, flagged in `adr/0019`'s Consequences in case the
underlying timing variability needs a sturdier fix later (e.g. a real AMQP-port healthcheck
instead of `rabbitmq-diagnostics -q ping`, which only confirms the Erlang node is up, not the
listener).

## Step 5 — Prove the deliverable end-to-end against real running containers

1. Fresh `docker compose down -v` + `up -d --build` - all 4 containers healthy, gateway started
   cleanly with the new auth wiring (`Loading proxy data from config` in its logs, no crash).
2. `GET /diagnostics/secure` with **no token** → **401**, rejected at the gateway.
3. Login as `broker` (wrong role for this endpoint) → real JWT issued; that token against
   `/diagnostics/secure` → **403**, rejected at the gateway (role mismatch, not just missing
   auth).
4. Login as `admin` → real JWT issued; that token against `/diagnostics/secure` → **200**, passes
   both the gateway's check and the `Api` host's own independent
   `RequireAuthorization(PlatformPolicies.RequireAdmin)`.
5. A **tampered** token (valid-looking JWT with an appended character, breaking the signature) →
   **401** at the gateway - proves the gateway validates the signature, not just header presence.
6. **Confirmed the rejections happen at the gateway, not just duplicated at the `Api` host**: the
   `Api`'s own audit log shows exactly **one** `SecureEndpointAccessed` entry (the genuinely
   successful admin request) - the no-token, wrong-role, and tampered-token attempts never reached
   the `Api` host at all.
7. `/health` and `/auth/login` (both unprotected at the gateway layer) continued working normally
   throughout - confirming the per-route scoping didn't accidentally widen to routes that must
   stay public.
8. `build/scripts/smoke-test.sh` (the actual CI script) re-run from a fresh environment - unpiped
   exit code 0, both the Postgres and RabbitMQ round-trip checks (Days 18/24) still pass unaffected
   by the gateway's new auth layer (neither of those endpoints is gateway-protected).
9. Full-solution `dotnet build`/`dotnet format --verify-no-changes`/`dotnet test`, all clean with
   unpiped exit codes (0/0/0) - `Platform.Messaging`'s coverage unaffected (95.6%, unrelated to
   this day's changes).

## Outcome

**Deliverable met**: `/diagnostics/secure` is now a genuinely defense-in-depth-protected sample
endpoint - enforced independently at both the gateway (edge) and the `Api` host, proven via a full
matrix (no token/wrong role/tampered token all rejected before reaching the `Api`; correct role
succeeds at both layers). **One recurring bug tightened** (RabbitMQ startup-race retry budget).
**Decision recorded**: `adr/0019-base-security-framework.md` (Accepted).

**Status**: Complete. Next unstarted item is **Day 26 — Structured Logging Rollout** (start of
Week 6, Observability), pending explicit instruction to begin.
