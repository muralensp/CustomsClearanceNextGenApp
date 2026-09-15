# Day 22 Execution Log — API Gateway

Phase 0, Week 5 (Core Infrastructure), Day 22 of the Day-Wise Plan. Deliverable: "Gateway routes
hello-world service."

---

## Step 1 — Confirm gateway technology before building

Asked the user directly rather than assuming: YARP vs. Ocelot vs. nginx. Confirmed **YARP** —
native to the .NET 8 stack (ADR-0001), no extra runtime, and .NET's own
`Microsoft.AspNetCore.RateLimiting` covers the rate-limiting requirement without a third library.
Recorded as `adr/0016-api-gateway.md`.

## Step 2 — Confirm this fits the Modular Monolith, not just assume it does

Day6_API_Standards_v1.1.docx Section 2 already specifies "gateway for external ingress only;
internal calls authenticated" — referenced by Gate 1's own Security checklist item. A separate
gateway deployable in front of the single `Api` host is exactly that standard, not a new
architectural surface or a Modular Monolith (ADR-0003) violation: the monolith is the one host
behind the gateway, not several services fronted by it.

## Step 3 — Build the gateway

- `src/Gateway/CustomsClearance.Gateway.csproj` — `Yarp.ReverseProxy` 2.2.0, plus
  `Platform.Logging`/`Platform.ErrorHandling` references for consistent correlation-ID/error
  behavior at the true ingress point.
- `src/Gateway/Program.cs` — `AddReverseProxy().LoadFromConfig(...)`, a global fixed-window rate
  limiter (100 req/10s per client IP, 429 on rejection), `UseCustomsClearanceCorrelationId()` +
  `UseCustomsClearanceErrorHandling()` (same middleware the `Api` host uses), and `GET
  /__gateway/health` mapped before the reverse-proxy catch-all for the gateway's own liveness.
- `src/Gateway/appsettings.json` — single catch-all route to an `api-cluster`, default address
  `http://localhost:8080/` for the non-containerized (`dotnet run`) case.
- `src/Gateway/Dockerfile` — same multi-stage pattern as `src/Api/Dockerfile` (Day 13).
- `docker-compose.yml` — `gateway` service now publishes host port `8080` (where `api` used to);
  `api` no longer publishes a host port, reachable only internally as `http://api:8080`, wired via
  a `ReverseProxy__Clusters__api-cluster__Destinations__api-destination__Address` environment
  override on `gateway` — same override pattern Day 18 used for the Postgres connection string.
- Added `src/Gateway/CustomsClearance.Gateway.csproj` to `CustomsClearance.sln` via `dotnet sln
  add` (not hand-edited).

## Step 4 — Prove the deliverable end-to-end, not just build it

1. `dotnet build CustomsClearance.sln` — 0 warnings, 0 errors (Gateway included).
2. `docker compose up -d --build` — fresh images for both `api` and `gateway`, `postgres`
   healthcheck gating startup as before.
3. `GET http://localhost:8080/__gateway/health` → 200, gateway's own liveness.
4. `GET http://localhost:8080/health` → 200 `{"status":"Healthy","service":"CustomsClearance.Api"}`
   — **the literal Day 22 deliverable**: the gateway (port 8080) routing to the `Api` host's
   hello-world endpoint, which is no longer directly published.
5. Confirmed `api` is genuinely no longer host-reachable: a probe on the old direct-access
   assumption failed to connect (no port mapping left for it).
6. `POST http://localhost:8080/diagnostics/widgets?...` → 200, proxied through to a real
   PostgreSQL round-trip — proving the gateway doesn't just forward simple GETs.
7. Rate limiting: a sequential 110-request loop all returned 200 (the loop itself took ~13s,
   longer than the 10s window, so it rolled over mid-burst — not a bug, a test-design artifact,
   caught by timing the loop). A **concurrent** burst of 150 requests (`xargs -P 30`) within the
   window produced **121× 200, 29× 429** — rate limiting confirmed working. Waited past the
   10-second window and confirmed the limiter resets (`/health` → 200 again).
8. Ran `build/scripts/smoke-test.sh` (the actual CI script) unmodified — passed, because it
   already targets `localhost:8080`, which is now serviced by the gateway rather than the `Api`
   host directly. No CI workflow file needed updating (neither `ci.yml` nor
   `azure-pipelines.yml` hardcodes project lists).
9. `dotnet format --verify-no-changes --severity warn` — clean. `dotnet test` — 9/9 passed.

## Step 5 — Real bug found and fixed during verification (correlation ID)

Step 4.4's header inspection (`curl -D -`) showed **two different UUIDs** for the same request's
`X-Correlation-Id` response header, not the expected single consistent value. Root cause:
`Platform.Logging`'s `CorrelationIdMiddleware` (Day 16) only ever wrote the correlation ID onto
the **response** headers, never the **request** headers. That's invisible in a single-host
setup, but breaks in front of a reverse proxy: YARP forwards the original request's headers
downstream by default, so a gateway-generated ID (missing from the original client request) never
reached the proxied `Api` destination, which then generated its own separate ID.

Fixed by also setting `context.Request.Headers[HeaderName]` in the same middleware
(`src/Platform/Logging/CorrelationIdMiddleware.cs`). Rebuilt containers, re-verified: both a
gateway-generated ID and a client-supplied one (`X-Correlation-Id: test-fixed-id-123`) now appear
identically end to end. `Platform.Logging.Tests` re-run after the fix: still 100%
line/branch/method coverage, all passing.

## Outcome

**Deliverable met**: gateway (YARP) routes to the `Api` host's hello-world endpoint, proven live
against real containers, not assumed. **Real bug found and fixed** (correlation-ID request-header
propagation), benefiting any future service sitting in front of another, not just this gateway.
**Decision recorded**: `adr/0016-api-gateway.md` (Accepted).

**Status**: Complete. Next unstarted item is **Day 23 — Identity & Access**, pending explicit
instruction to begin.
