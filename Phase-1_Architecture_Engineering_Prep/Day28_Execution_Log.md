# Day 28 Execution Log — Distributed Tracing

Phase 0, Week 6 (Observability), Day 28 of the Day-Wise Plan - explicitly parallelizable with Days
26/27/29, merged on Day 30. Deliverable: instrument services with distributed tracing
(OpenTelemetry) and a tracing backend.

---

## Step 1 — Confirm tracing backend before building

Asked the user directly: Grafana Tempo vs. Jaeger. Confirmed **Tempo** - stays inside the existing
Grafana stack (Loki + Prometheus already provisioned there from Days 26/27) rather than adding a
second, separate query UI. Recorded as `adr/0022-distributed-tracing.md`.

## Step 2 — Extend Platform.Observability with tracing

Added `AddCustomsClearanceTracing(serviceName, otlpEndpoint)` to the existing
`src/Platform/Observability/ServiceCollectionExtensions.cs` (alongside Day 27's
`AddCustomsClearanceMetrics`):
- ASP.NET Core instrumentation (inbound spans) + HttpClient instrumentation (outbound spans) - the
  latter is what turns the Gateway's YARP-proxied call to the Api host into a real child span
  instead of two disconnected traces.
- Every span enriched with a `correlation_id` tag read off the existing `X-Correlation-Id` request
  header (`CorrelationIdMiddleware`, Day 16), so a trace shares the same correlation key Loki's log
  lines already use.
- OTLP exporter (gRPC, 4317), endpoint config-driven via `Otlp:Endpoint`
  (`http://localhost:4317` default for `dotnet run`, `http://tempo:4317` in
  `docker-compose.yml`).

`Platform.Observability` took a new `ProjectReference` on `Platform.Logging` (for
`CorrelationIdMiddleware.HeaderName`), and new packages: `OpenTelemetry.Instrumentation.Http`,
`OpenTelemetry.Exporter.OpenTelemetryProtocol` (both 1.9.0, matching the pinned OTel core version
from Day 27).

Wired into both `Program.cs` files right after Day 27's metrics call.

## Step 3 — Add Tempo and wire the Grafana datasource

- `observability/tempo.yaml` - minimal single-binary config, OTLP receiver on 4317, local-disk
  storage, HTTP query API on 3200. Same dev/CI-scale posture as everything else in this stack.
- `docker-compose.yml` - new `tempo` service; `api`/`gateway` get `Otlp__Endpoint` env overrides
  and a `depends_on` entry on `tempo`.
- `observability/grafana-datasources.yaml` - third datasource (Tempo), with `tracesToLogsV2`
  configured to query Loki for the same `correlation_id` span tag every trace carries, so a trace
  can pivot straight to its request's log lines inside Grafana's trace view.

## Step 4 — Recurring RabbitMQ startup issue, same category as Days 26/27, resolved fast

The first `docker compose up -d --build` of this day failed: RabbitMQ crashed with the same
`.erlang.cookie: eacces` error documented on Days 26/27. `docker ps -a` / `docker volume ls`
inspection (not a blind prune) found a leftover anonymous volume from a prior interrupted cycle on
this machine, unattached to any running container, colliding with the fresh `rabbitmq` container's
volume allocation. Removed it (`docker volume rm`); the very next `docker compose up` started
RabbitMQ healthy with no further changes. Unlike Day 27's finding (an orphaned *container*), this
time it was a stray *volume* directly - same root category (leftover Docker state on this machine),
resolved in one inspection pass now that the diagnostic approach is established.

## Step 5 — Prove the deliverable end-to-end against real running containers

1. Fresh `docker compose up -d --build` (after Step 4's fix) - all 9 containers (`gateway`, `api`,
   `postgres`, `rabbitmq`, `loki`, `promtail`, `grafana`, `prometheus`, `tempo`) up and healthy.
2. `GET http://localhost:8080/health` with a known `X-Correlation-Id` header, through the Gateway
   (not directly against the Api - the whole point is proving the cross-service hop).
3. Queried Tempo's own API directly: `GET /api/search?tags=correlation_id=<the-id>` - found the
   trace (`traceID`, `rootServiceName: CustomsClearance.Gateway`).
4. Fetched the full trace (`GET /api/traces/<traceID>`) and parsed it: **three spans across two
   services** for that one request - `CustomsClearance.Gateway` inbound server span,
   `CustomsClearance.Gateway` outbound client span (the proxy hop), `CustomsClearance.Api` inbound
   server span. Confirms the trace genuinely spans gateway->api, not just one service's own
   request.
5. Re-ran the identical search **through Grafana's own datasource proxy**
   (`GET /api/datasources/proxy/uid/Tempo/api/search?tags=...`) - the real path a Grafana user's
   trace-explore view uses - and got the same result, confirming the datasource is genuinely wired
   and queryable end-to-end, not just declared in provisioning YAML.
6. Re-checked Prometheus's scrape targets (`GET /api/v1/targets`) - both `customsclearance-api` and
   `customsclearance-gateway` still reported `health: "up"`, confirming Day 27's metrics pipeline
   wasn't disturbed by this day's changes.
7. `docker compose down -v`, then `build/scripts/smoke-test.sh` (the actual CI script) - unpiped
   exit code 0, Postgres/RabbitMQ round-trip checks (Days 18/24) unaffected.
8. Full-solution `dotnet build`/`dotnet format --verify-no-changes`/`dotnet test`, all clean with
   unpiped exit codes (0/0/0). No new test project for `Platform.Observability`'s tracing method -
   same judgment as Day 27's metrics method (thin OpenTelemetry configuration wiring, no branching
   logic to meaningfully unit-test; correctness proven live above).

## Note: NU1902 advisory on OpenTelemetry.Exporter.OpenTelemetryProtocol 1.9.0

`dotnet build` flagged a moderate-severity advisory (`GHSA-4625-4j76-fww9`) on the new OTLP
exporter package. Investigated (not ignored): the vulnerability is a local-blob-injection risk in
the exporter's **disk retry** feature, which only activates when
`OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY=disk` is explicitly set as an environment variable - this
project sets no such variable anywhere. Confirmed not exploitable in this configuration; left
un-upgraded rather than force-bumping to a newer major OTel version that risks breaking
compatibility with the pinned `OpenTelemetry.Exporter.Prometheus.AspNetCore` 1.9.0-beta.2 (Day 27).
Documented in `adr/0022-distributed-tracing.md`'s Consequences for future revisit.

## Outcome

**Deliverable met**: real distributed tracing across both running services, a request's trace
provably spanning the Gateway->Api hop as one connected trace (not two disconnected ones), tagged
with the same correlation key already used for log correlation (Day 26), queryable through
Grafana's own datasource proxy - not just present in a config file. **One recurring environmental
issue resolved quickly** using the diagnostic pattern established on Days 26/27. **Decision
recorded**: `adr/0022-distributed-tracing.md` (Accepted).

**Status**: Complete. Per the Day-Wise Plan, Days 26-29 (Observability) remain parallelizable, with
Day 30 as the mandatory sequential wiring/review step. Next unstarted item is **Day 29 —
Alerting**, pending explicit instruction to begin.
