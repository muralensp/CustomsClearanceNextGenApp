# ADR-0016: API Gateway (Day 22)

**Status:** Accepted
**Date:** 2026-09-15
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Day 22 of the Day-Wise Plan ("API Gateway") calls for: stand up an API Gateway (routing,
rate limiting, basic auth passthrough), with deliverable "Gateway routes hello-world service."

Two things needed deciding before building:

1. **Gateway technology.** Asked the user directly. Confirmed **YARP** (Yet Another Reverse
   Proxy) — Microsoft's own .NET reverse-proxy library, native to the ADR-0001 stack, no new
   runtime/infra beyond another ASP.NET Core project, and .NET's built-in
   `Microsoft.AspNetCore.RateLimiting` middleware covers the rate-limiting requirement without a
   third dependency (ruled out Ocelot as a heavier dependency for a solo-scale setup, and nginx as
   introducing a non-.NET config surface into an all-.NET stack).
2. **Whether a separate gateway deployable fits the Modular Monolith (ADR-0003).** It does: the
   monolith is the single `src/Api` host being fronted, not multiple services fronted by a
   gateway. Day6_API_Standards_v1.1.docx Section 2 already anticipated exactly this shape
   ("gateway for external ingress only; internal calls authenticated"), referenced by Gate 1's
   Security checklist item — so this isn't a new architectural surface, it's the first
   implementation of an already-standing standard.

## Decision

1. **New project**: `src/Gateway/CustomsClearance.Gateway.csproj` — `Microsoft.NET.Sdk.Web`,
   references `Yarp.ReverseProxy` (2.2.0) plus `Platform.Logging`/`Platform.ErrorHandling` for
   consistent correlation-ID/error-response behavior at the true ingress point.
2. **The gateway becomes the sole published edge.** `docker-compose.yml`: `gateway` now publishes
   host port `8080` (unchanged from where `api` used to publish it — no other file/script needed
   to change its target URL); `api` no longer publishes a host port, reachable only on the
   internal compose network as `http://api:8080`, which the gateway's reverse-proxy cluster
   (`ReverseProxy__Clusters__api-cluster__Destinations__api-destination__Address`) targets via an
   environment override — same override pattern as Day 18's `ConnectionStrings__DiagnosticsDb`.
3. **Routing**: a single catch-all route (`/{**catch-all}` → `api-cluster`) — Day 22 has exactly
   one backend to route to; per-context routing rules are a later-phase concern once bounded
   contexts have their own endpoints.
4. **Rate limiting**: `Microsoft.AspNetCore.RateLimiting`'s global fixed-window limiter, 100
   requests / 10 seconds per client IP, `429 Too Many Requests` on rejection, `QueueLimit: 0`
   (reject immediately rather than queue). Explicitly a conservative dev-time default, not a
   tuned production policy — real limits are a Day 26-30 (Observability/capacity) concern once
   real traffic patterns exist.
5. **Basic auth passthrough**: the gateway does not perform authentication itself — it forwards
   the `Authorization` header unmodified (YARP's default behavior for non-restricted headers);
   JWT validation stays at the `Api` host (`Platform.Security`, per ADR-0011), per Day 6's "gateway
   for external ingress only; internal calls authenticated."
6. **Own liveness endpoint**: `GET /__gateway/health`, distinct from the proxied `/health` (which
   reaches the `Api` host's own health check) — mapped before `MapReverseProxy()`'s catch-all so
   routing picks the more specific match.

### Real bug found and fixed during verification

`Platform.Logging`'s `CorrelationIdMiddleware` (Day 16) only echoed a generated correlation ID on
the **response**, never set it on the **request** headers. That's invisible when the middleware
runs in a single host (the response header is all a client needs), but breaks once it runs in
front of a reverse proxy: YARP forwards the original request's header collection downstream by
default, so a gateway-generated ID (absent from the original client request) never reached the
proxied `Api` destination — which then minted its *own* separate ID, producing two different
correlation IDs for one logical request. Confirmed via direct `curl -D -` header inspection
against the running containers (two distinct UUIDs in the response), not assumed. Fixed by also
setting `context.Request.Headers[HeaderName]` in the same middleware
(`src/Platform/Logging/CorrelationIdMiddleware.cs`); re-verified afterward — same ID appears end
to end, both for a gateway-generated ID and for a client-supplied one. `Platform.Logging.Tests`
still passes at 100% line/branch/method coverage after the fix.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Ocelot | More built-in gateway patterns (aggregation, etc.) out of the box | Heavier dependency for a single-backend, solo-scale setup; less aligned with Microsoft's own current direction than YARP | User declined |
| nginx (containerized) | Battle-tested, language-agnostic | Introduces a non-.NET config surface (`nginx.conf`) into an all-.NET stack (ADR-0001); another container/runtime to maintain | User declined |
| Keep `api` also published on a host port (additive gateway, not the sole edge) | Zero risk to existing CI/smoke-test path; simplest diff | Contradicts Day6_API_Standards_v1.1.docx Section 2's already-standing rule that only the gateway should be externally reachable; would defer, not implement, that standard | Rejected — implementing the already-documented standard was straightforward (smoke-test.sh needed no changes, since it targets `localhost:8080`, now serviced by the gateway) |

## Consequences

- `build/scripts/smoke-test.sh` required **no changes** — it already targets `localhost:8080`,
  which now resolves to the gateway instead of the `Api` host directly, and still passes (health
  check + real PostgreSQL round-trip, both proxied through the gateway).
- `.github/workflows/ci.yml`/`build/azure-pipelines.yml` required no changes — neither hardcodes
  project lists; both operate on the solution/scripts generically.
- Any future bounded-context endpoint reachable externally goes through this gateway by
  construction (it's now the only published edge) — no separate decision needed each time a new
  context adds routes, as long as they're served by the same `Api` host.
- The rate-limit values (100/10s) and single-cluster routing are dev-time placeholders, flagged
  for revisit once Days 26-30 (Observability) or real bounded-context traffic gives a basis for
  real numbers/routing rules.
- The correlation-ID request-header fix in `Platform.Logging` benefits every future service that
  sits in front of another (not gateway-specific) — worth noting in case a similar "response-only"
  pattern is copied elsewhere before this ADR is read.

## Related

[[ADR-0003]] (Modular Monolith — why the gateway fronts one host, not several), [[ADR-0006]]
(Clean Architecture adaptation), [[ADR-0008]] (containerization/`docker-compose.yml` this ADR
extends), [[ADR-0015]] (Day 21 local dev environment — this ADR is the next entry in that same
Week 5 infrastructure buildout), Day6_API_Standards_v1.1.docx Section 2 (the standard this ADR
implements), `docs/quality-gates/Gate1_Architecture_Review.md` (Security section referencing this
transport rule).
