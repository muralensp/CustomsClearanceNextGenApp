# ADR-0021: Monitoring & Metrics (Day 27)

**Status:** Accepted
**Date:** 2026-09-16
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 6 (Observability), Day 27 of the Day-Wise Plan ("Monitoring & Metrics") - one of
four days explicitly marked parallelizable with Days 26/28/29, merged on Day 30. Calls for: set up
metrics collection (Prometheus/equivalent) and a basic dashboard (Grafana). Deliverable: "Dashboard
showing service health/latency."

Grafana already existed from Day 26 (ADR-0020, for the centralized log view), so this day's real
scope was: (1) a metrics-collection library for the `Api`/`Gateway` services, (2) Prometheus to
scrape and store those metrics, (3) a dashboard on the Grafana instance that already exists.

One decision confirmed with the user: **OpenTelemetry .NET + the Prometheus exporter**
(`OpenTelemetry.Exporter.Prometheus.AspNetCore`) over `prometheus-net`. OpenTelemetry is
Microsoft's current recommended instrumentation path for .NET and gives built-in ASP.NET Core HTTP
request duration/count metrics with no custom instrumentation code - `prometheus-net`, while a
long-established and still-fine community package, isn't the direction .NET tooling itself is
converging on.

## Decision

1. **New `Platform.Observability` library** (`src/Platform/Observability/`), matching this
   project's `AddCustomsClearance*` convention rather than duplicating OpenTelemetry wiring in
   both `Api` and `Gateway` `Program.cs` files:
   - `AddCustomsClearanceMetrics(serviceName)` - registers OpenTelemetry with ASP.NET Core
     instrumentation (`AddAspNetCoreInstrumentation()`) and the Prometheus exporter.
     `serviceName` becomes the OpenTelemetry Resource's `service.name` attribute, which
     Prometheus surfaces as the `job` label - this is how one shared Grafana dashboard
     distinguishes `customsclearance-api` metrics from `customsclearance-gateway` metrics.
   - `MapCustomsClearanceMetricsEndpoint()` - maps the Prometheus scrape endpoint (`/metrics`),
     public/unauthenticated by design (operational metrics, not business data - every Prometheus
     deployment, including this project's own, expects to reach it directly).
2. **Both `Api` and `Gateway`** call `AddCustomsClearanceMetrics("CustomsClearance.{Api|Gateway}")`
   and map the endpoint before any catch-all routing (same reasoning as `/__gateway/health` on the
   Gateway - a more specific route must resolve before YARP's catch-all proxy route, and on the
   Gateway `/metrics` is deliberately **not** behind the Day 25 `AuthorizationPolicy` mechanism,
   since it's a plain Minimal API endpoint outside `ReverseProxy:Routes`, unaffected by that
   config entirely).
3. **`docker-compose.yml`** gains `prometheus` (`prom/prometheus:v2.54.1`), scraping both services
   directly on the internal network (`api:8080/metrics`, `gateway:8080/metrics` -
   `observability/prometheus.yml`) - the same "no host port needed, containers talk to each other
   by compose service name" pattern already used for Postgres/RabbitMQ/Loki. Grafana gets a second
   datasource (Prometheus, alongside Day 26's Loki) and a **provisioned dashboard**
   (`observability/dashboards/api-gateway-overview.json`, loaded via
   `observability/grafana-dashboards-provider.yaml`) - reproducible via `docker compose up` alone,
   the same zero-manual-setup discipline as Day 26's datasource provisioning.
4. **Dashboard queries were written from real, verified metric names**, not guessed from
   documentation: brought the stack up first, queried Prometheus's own
   `/api/v1/label/__name__/values` and a sample `http_server_request_duration_seconds_count`
   query to confirm the actual metric names and label set (`job`, `http_route`,
   `http_response_status_code`, `http_request_method`) OpenTelemetry's ASP.NET Core instrumentation
   1.9.0 actually produces, *then* wrote the dashboard JSON against those confirmed names. Five
   panels: Service Up (`up{job=~"customsclearance-.*"}`), Request Rate, p95 Latency
   (`histogram_quantile` over the duration histogram), Error Rate (5xx), and Active Requests -
   covering the deliverable's "service health/latency" wording directly.

### Real issues found and fixed during verification (environmental, not design bugs)

1. **Recurring stray-volume issue, now root-caused.** The same RabbitMQ `.erlang.cookie: eacces`
   crash from Day 26 recurred twice more during this day's verification. Root cause this time,
   traced fully rather than just pruned again: an unrelated, months-old orphaned container
   (`my-postgres`, unconnected to this project, last exited 7 weeks prior) was holding a Docker
   volume that collided with anonymous-volume allocation for the `rabbitmq` service on this
   machine. Removed the orphaned container and the stray volumes explicitly; re-verified clean
   across a subsequent fresh `docker compose up` with no recurrence. Flagged in case this
   machine-specific leftover state resurfaces - the fix is `docker ps -a` / `docker volume ls`
   inspection, not a `docker-compose.yml` change.
2. **No text-based HTTP client inside the `Api`/`Gateway` runtime images** (`aspnet:8.0` base,
   deliberately minimal) - `curl`/`wget` both absent, so `/metrics` couldn't be inspected via
   `docker exec`. Not a bug, just a verification-method adjustment: queried Prometheus's own API
   (which already had the scraped data) instead, which is also a more realistic proof anyway
   (proving the actual scrape pipeline works, not just that the endpoint responds to a manual
   probe).

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| `prometheus-net` | Long-established, widely used, simple API | Not the direction Microsoft's own .NET tooling is converging on; OpenTelemetry is becoming the standard instrumentation layer | User declined |
| Guess dashboard queries from OpenTelemetry semantic-convention docs without verifying against the real running stack | Faster to write | Semantic convention metric names have changed across OTel instrumentation versions; a guessed query risks an empty/broken panel that looks fine until someone actually opens the dashboard | Rejected - this project's standing discipline is proving things against the real running system, not assuming from documentation |

## Consequences

- `Platform.Observability` has no dedicated unit test project - its two extension methods are thin
  OpenTelemetry configuration wiring with no branching logic to unit-test meaningfully (the same
  judgment already applied to `Api`/`Gateway`'s own `Program.cs`, which also has no dedicated unit
  tests). Correctness is proven by the live `docker compose` verification above (real scrape
  targets healthy, real dashboard panels returning real data), not a test suite - consistent with
  how `RabbitMqEventBus`/`RabbitMqEventConsumer` (Day 24) were proven the same way.
- Any future bounded context's endpoints are automatically covered by the existing HTTP
  instrumentation the moment they're added to the `Api` host - no per-endpoint metrics code needed.
- The dashboard is a starting point (5 panels covering the literal deliverable), not a
  comprehensive SRE dashboard - alerting on these metrics is explicitly Day 29's scope, not this
  one's.
- `/metrics` being unauthenticated on both services is a deliberate, not overlooked, choice -
  revisit only if this stack is ever exposed beyond a trusted network (still not the case; no
  hosting/cloud decision has been made per ADR-0008/0015).

## Related

[[ADR-0020]] (Day 26 - the Grafana instance and provisioning pattern this ADR extends with a
second datasource and a dashboard), [[ADR-0016]] (Day 22 Gateway - `/metrics`'s placement outside
`ReverseProxy:Routes`, unaffected by Day 25's per-route `AuthorizationPolicy` mechanism),
Day-Wise Plan Week 6 (Days 26-29, explicitly parallelizable, merged on Day 30).
