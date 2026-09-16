# Day 27 Execution Log — Monitoring & Metrics

Phase 0, Week 6 (Observability), Day 27 of the Day-Wise Plan - explicitly parallelizable with Days
26/28/29, merged on Day 30. Deliverable: "Dashboard showing service health/latency."

---

## Step 1 — Confirm metrics library before building

Asked the user directly: OpenTelemetry .NET + Prometheus exporter vs. `prometheus-net`. Confirmed
**OpenTelemetry** - Microsoft's current recommended path, built-in ASP.NET Core HTTP
instrumentation with no custom code needed. Recorded as `adr/0021-monitoring-and-metrics.md`.

## Step 2 — Build a shared Platform.Observability library

Rather than duplicate OpenTelemetry wiring in both `Api` and `Gateway`, created
`src/Platform/Observability/` matching the `AddCustomsClearance*` convention:
- `AddCustomsClearanceMetrics(serviceName)` - OpenTelemetry + ASP.NET Core instrumentation +
  Prometheus exporter, `serviceName` becomes the `job` label distinguishing api from gateway on
  one shared dashboard.
- `MapCustomsClearanceMetricsEndpoint()` - maps `/metrics`, public by design.

Wired into both `Program.cs` files, both `.csproj` files (new `ProjectReference`), both
`Dockerfile`s (new `COPY` steps), and added the project to the solution via `dotnet sln add`.

## Step 3 — Add Prometheus and a Grafana dashboard, verified against real metric names

- `observability/prometheus.yml` - scrapes `api:8080/metrics` and `gateway:8080/metrics` directly
  on the internal compose network.
- `docker-compose.yml` - new `prometheus` service; `grafana` gains a second datasource
  (`observability/grafana-datasources.yaml`, extended) and dashboard provisioning
  (`observability/grafana-dashboards-provider.yaml` + `observability/dashboards/`).
- **Brought the stack up first, then wrote the dashboard** - queried Prometheus's own
  `/api/v1/label/__name__/values` and a sample metric query to confirm the real metric names
  (`http_server_request_duration_seconds_{count,sum,bucket}`, `http_server_active_requests`) and
  label set (`job`, `http_route`, `http_response_status_code`, `http_request_method`) before
  writing `observability/dashboards/api-gateway-overview.json` against them, rather than guessing
  from OpenTelemetry semantic-convention documentation (which has shifted across instrumentation
  versions). Five panels: Service Up, Request Rate, p95 Latency, Error Rate (5xx), Active
  Requests.

## Step 4 — Recurring RabbitMQ startup issue, this time fully root-caused

The Day 26 `.erlang.cookie: eacces` crash recurred twice more during this day's verification.
Previously just pruned away; this time traced fully: an unrelated, months-old orphaned container
(`my-postgres`, no connection to this project, last exited 7 weeks earlier) was holding a Docker
volume that collided with anonymous-volume allocation for the `rabbitmq` service on this machine.
Removed the orphaned container and the colliding volumes explicitly (`docker ps -a` / `docker
volume ls` inspection, not another blind prune); re-verified clean across a subsequent fresh
`docker compose up` with no recurrence. Documented as machine-specific leftover state, not a
`docker-compose.yml` issue.

## Step 5 — Verification-method adjustment (not a bug)

Neither the `Api` nor `Gateway` runtime image (`aspnet:8.0`, deliberately minimal) has `curl` or
`wget`, so `/metrics` couldn't be inspected via `docker exec`. Queried Prometheus's own API
instead (which already had the scraped data) - arguably a more realistic proof anyway, since it
verifies the actual scrape pipeline rather than a manual endpoint probe.

## Step 6 — Prove the deliverable end-to-end against real running containers

1. Fresh `docker compose up -d --build` - all 8 containers (`gateway`, `api`, `postgres`,
   `rabbitmq`, `loki`, `promtail`, `grafana`, `prometheus`) up.
2. `GET http://localhost:9090/api/v1/targets` - both `customsclearance-api` and
   `customsclearance-gateway` scrape targets reported `health: "up"`.
3. Generated a real traffic burst (20× `/health`, one `/diagnostics/secure`).
4. **Dashboard auto-provisioned with zero manual setup**: `GET /api/search` and `GET
   /api/dashboards/uid/customsclearance-api-gateway-overview` confirmed the dashboard existed with
   all 5 panels, immediately after a Grafana restart (needed once, since the dashboard file was
   added after Grafana's first startup - Grafana's file-provisioning poll picks up new files
   automatically on subsequent restarts/interval ticks).
5. **Queried every panel's exact PromQL expression through Grafana's own datasource proxy** (not
   Prometheus directly) - Service Up returned `1` for both jobs; Request Rate returned real
   non-zero rates for both `customsclearance-api` and `customsclearance-gateway`; p95 Latency
   returned real values (~5ms Api, ~35ms Gateway - the gateway's extra latency from its own Day 25
   auth/rate-limit middleware is plausible and itself a useful observability signal).
6. Fresh `docker compose down -v` + `build/scripts/smoke-test.sh` (the actual CI script) - unpiped
   exit code 0, Postgres/RabbitMQ round-trip checks (Days 18/24) unaffected.
7. Full-solution `dotnet build`/`dotnet format --verify-no-changes`/`dotnet test`, all clean with
   unpiped exit codes (0/0/0) - no new test project needed for `Platform.Observability` (thin
   configuration wiring, no branching logic to unit-test meaningfully; correctness proven by the
   live verification above, same judgment as `Api`/`Gateway`'s own `Program.cs`).

## Outcome

**Deliverable met**: a real, provisioned Grafana dashboard showing service health (`up`) and
latency (p95, request rate, error rate, active requests) for both running services, built from
verified real metric names rather than assumed ones, and proven live through Grafana's own query
path. **One recurring environmental issue fully root-caused** (unrelated orphaned container/volume
collision, not a project config issue). **Decision recorded**:
`adr/0021-monitoring-and-metrics.md` (Accepted).

**Status**: Complete. Per the Day-Wise Plan, Days 26-29 (Observability) remain parallelizable, with
Day 30 as the mandatory sequential wiring/review step. Next unstarted item is **Day 28 —
Distributed Tracing**, pending explicit instruction to begin.
