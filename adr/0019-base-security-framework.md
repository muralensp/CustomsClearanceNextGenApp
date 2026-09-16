# ADR-0019: Base Security Framework (Day 25)

**Status:** Accepted
**Date:** 2026-09-16
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Day 25 of the Day-Wise Plan ("Base Security Framework") calls for: wire `security-lib`
into gateway and identity; enforce authenticated routes end-to-end. Deliverable: "Auth-protected
sample endpoint working."

Identity (Day 23) already used `Platform.Security` fully (issuance + validation, ADR-0017). The
open question was the gateway: ADR-0016 (Day 22) deliberately decided the gateway would **not**
authenticate - only pass the `Authorization` header through unmodified, with all JWT validation
staying at the `Api` host. Day 25's literal wording ("wire security-lib into gateway") reads as
extending that decision, not just re-proving it, so this was confirmed with the user directly
rather than assumed:

**Chosen: add real JWT validation at the gateway** (defense-in-depth - reject invalid/missing
tokens at the edge, before ever proxying to the `Api` host), over the alternative of leaving the
gateway a pure pass-through and treating Day 25 as a verification-only pass. This does not reverse
ADR-0016's core decision (the gateway is still the sole external ingress point, and the `Api` host
still performs its own independent enforcement) - it adds a second layer using the same
`Platform.Security` policies, not a competing one.

## Decision

1. **`src/Gateway`** now references `Platform.Security` and calls
   `AddCustomsClearanceAuthentication`/`AddCustomsClearanceAuthorization` +
   `UseAuthentication`/`UseAuthorization`, the same calls the `Api` host makes - both trust
   boundaries share one `"Jwt"` config section (same symmetric key/issuer/audience), so a token
   valid at one is valid at the other.
2. **Per-route enforcement via YARP's native `AuthorizationPolicy` config**, not custom
   middleware: `appsettings.json`'s `ReverseProxy:Routes` now has two routes instead of one -
   `diagnostics-secure-route` (`Match.Path: /diagnostics/secure`, `AuthorizationPolicy:
   RequireAdmin`) ahead of the existing catch-all `api-route` (no policy - ASP.NET Core's endpoint
   routing inherently prefers the more specific literal path over `{**catch-all}`, no explicit
   `Order` needed). This is the idiomatic YARP mechanism for per-route auth - no bespoke
   path-matching code.
3. **Scope: the one existing protected sample endpoint** (`/diagnostics/secure`,
   `PlatformPolicies.RequireAdmin`), matching the Day-Wise Plan's own singular wording ("a sample
   endpoint"). Every other route (`/health`, `/auth/login`, the `/diagnostics/*` widgets/
   validation-error endpoints) remains open at the gateway layer - the `Api` host is still the
   source of truth for which of *its* endpoints require auth; the gateway only pre-empts the ones
   explicitly marked. A blanket "gateway requires auth for everything" policy would have blocked
   `/auth/login` itself (chicken-and-egg: no token exists yet to obtain a token) and every future
   public endpoint without a per-route opt-out, so route-level opt-in (not global opt-out) is the
   right default here.
4. **Both layers stay independent, not layered-then-skipped**: the `Api` host's own
   `RequireAuthorization(PlatformPolicies.RequireAdmin)` on `/diagnostics/secure` is unchanged and
   still runs for any request the gateway forwards - confirmed via the `Api`'s own audit log
   showing only the one genuinely-successful request, proving the gateway's rejections never even
   reached the `Api` host, and that the `Api` host doesn't blindly trust "the gateway already
   checked."

### Real bug found and fixed during verification (recurrence, not new)

The Day 24 RabbitMQ startup race (`docker-compose`'s healthcheck reporting healthy before the AMQP
listener actually accepts connections) recurred during this day's verification - the retry budget
`RabbitMqConnectionHelper` used (10 attempts × 2s = 20s) was occasionally insufficient. Increased
to 20 attempts (40s budget) for headroom; re-verified across two consecutive fresh `docker compose
down -v` / `up --build` cycles with no further failures. Not a new bug, a tightened margin on an
already-fixed one - flagged in case the underlying timing variability ever needs a sturdier fix
(e.g. a dedicated AMQP-port healthcheck instead of `rabbitmq-diagnostics -q ping`, which only
confirms the Erlang node is up).

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Keep gateway pass-through-only; Day 25 = verification/hardening pass only | No architecture change, lowest risk | Doesn't literally wire `Platform.Security` into the gateway as the plan's wording asks; Day 22's existing end-to-end proof (login→JWT→200/403 through the gateway) already substantially covered "verification" | User declined |
| Gateway requires auth for every route by default, explicit opt-out for public ones | Arguably a "secure by default" posture | Would need to enumerate every public route (health, login, future public endpoints) as an exception list that grows every time a new public endpoint is added; blocks `/auth/login` unless immediately special-cased | Rejected - opt-in per protected route matches the Day 25 scope ("a sample endpoint") without inventing a maintenance burden for routes that don't exist yet |
| Custom middleware doing manual path-prefix + JWT checks instead of YARP's `AuthorizationPolicy` | Full control over matching logic | Reinvents what YARP already does natively via config, with more code to maintain and test | Rejected - YARP's built-in per-route `AuthorizationPolicy` is the documented, idiomatic mechanism |

## Consequences

- Adding gateway-level protection to a future endpoint is a one-line config addition
  (`ReverseProxy:Routes` entry with a matching `AuthorizationPolicy`), not a code change - worth
  remembering as more bounded contexts get real endpoints.
- The gateway and `Api` host's `Jwt` config must stay in sync (same key/issuer/audience) for tokens
  to validate at both layers - currently duplicated in each project's `appsettings.json`
  (Development placeholder value, per ADR-0004 Item 5's secrets-management framing); a shared
  secrets store would remove this duplication once one exists (still deferred, per ADR-0008/0015).
- No dedicated Gateway test project exists yet (same as the `Api` host - only
  `WebApplicationFactory`-based integration tests and the container smoke-test layer cover it) -
  Day 25's proof is entirely the live `docker compose` run plus the existing test suite's
  unaffected pass, consistent with ADR-0009's layering.

## Related

[[ADR-0011]] (Security & Audit libraries - `Platform.Security`'s role/policy model this ADR reuses
at a second layer), [[ADR-0016]] (Day 22 Gateway - the pass-through decision this ADR extends, not
reverses), [[ADR-0017]] (Day 23 Identity - the login flow this ADR's enforcement protects),
[[ADR-0018]] (Day 24 Event Bus - source of the recurring startup-race finding), Day6_API_Standards_
v1.1.docx Section 2 (gateway/internal-call auth rules), `docs/quality-gates/Gate1_Architecture_
Review.md` (Security section referencing this transport rule).
