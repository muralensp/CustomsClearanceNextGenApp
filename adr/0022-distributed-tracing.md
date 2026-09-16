# ADR-0022: Distributed Tracing (Day 28)

**Status:** Accepted
**Date:** 2026-09-16
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 6 (Observability), Day 28 of the Day-Wise Plan ("Distributed Tracing") - one of four
days explicitly marked parallelizable with Days 26/27/29, merged on Day 30. Calls for: instrument
services with distributed tracing (OpenTelemetry) and a tracing backend.

Days 26/27 already gave the `Api`/`Gateway` services structured logs (Loki) and metrics
(Prometheus), both centralized in the same Grafana instance. The two-hop request path introduced
by Day 22's Gateway (client -> Gateway -> Api) is exactly the shape distributed tracing exists to
make visible - a single request's cross-service timeline, not just each service's own logs/metrics
in isolation.

One decision confirmed with the user: **Grafana Tempo** as the tracing backend, over Jaeger.
Tempo stays inside the existing Grafana stack (a Grafana datasource, alongside Day 26/27's
Loki/Prometheus) rather than introducing a second, separate query UI - the same "prefer what's
already provisioned" reasoning Day 26 used for Loki over ELK.

## Decision

1. **`Platform.Observability` gains `AddCustomsClearanceTracing(serviceName, otlpEndpoint)`**,
   alongside Day 27's `AddCustomsClearanceMetrics`, matching the same `AddCustomsClearance*`
   convention:
   - ASP.NET Core instrumentation (inbound request spans) + HttpClient instrumentation (outbound
     calls) - the latter is what turns the Gateway's YARP-proxied call to the `Api` host into a
     real child span, rather than two disconnected single-hop traces.
   - Every span is enriched with a `correlation_id` tag read from the existing `X-Correlation-Id`
     request header (`CorrelationIdMiddleware`, Day 16) - the same correlation key already used to
     tie a request's log lines together in Loki (Day 26), now also usable to look up its trace in
     Tempo. `Platform.Observability` takes a new `ProjectReference` on `Platform.Logging` for
     `CorrelationIdMiddleware.HeaderName`.
   - Exports via OTLP (gRPC, port 4317) to Tempo - the exporter's `Endpoint` is config-driven
     (`Otlp:Endpoint`, defaulting to `http://localhost:4317` for `dotnet run`), the same
     `Host=<compose-service>` (container) vs. `Host=localhost` (bare `dotnet run`) split already
     used for Postgres/RabbitMQ/Tempo's own siblings.
2. **Both `Api` and `Gateway`** call `AddCustomsClearanceTracing("CustomsClearance.{Api|Gateway}",
   ...)` right after Day 27's `AddCustomsClearanceMetrics` call - `service.name` becomes each
   trace's `rootServiceName`/span resource attribute, the same distinguishing label Prometheus
   already uses via the `job` label.
3. **`docker-compose.yml`** gains `tempo` (`grafana/tempo:2.5.0`), a minimal single-binary config
   (`observability/tempo.yaml`) receiving OTLP on 4317 and serving queries on its built-in HTTP API
   (3200), local-disk storage only - same dev/CI-scale, no-retention-tuning posture as every other
   piece of this stack. Both `api` and `gateway` get an `Otlp__Endpoint=http://tempo:4317`
   environment override and a `depends_on` entry on `tempo`.
4. **Grafana gets a third datasource** (`observability/grafana-datasources.yaml`, extended) -
   Tempo, with `tracesToLogsV2` configured to query Loki for the same `correlation_id` a trace's
   spans carry, so a user can pivot from a trace straight to its request's log lines from inside
   Grafana's trace view. No dashboard panel added this day (Day 27's dashboard already covers
   "service health/latency"; a trace-specific dashboard isn't the deliverable here - Tempo's own
   Grafana trace-explore view is).

## Verification

Full `docker compose down -v` / `up -d --build` cycle, then:
- Generated a real request through the Gateway (`GET /health` with a known `X-Correlation-Id`) and
  queried Tempo's own `/api/search?tags=correlation_id=...` - found the trace.
- Fetched the full trace and confirmed **three spans across two services** for that one request:
  `CustomsClearance.Gateway`'s inbound server span, `CustomsClearance.Gateway`'s outbound client
  span (the YARP proxy hop, via `HttpClient` instrumentation), and `CustomsClearance.Api`'s inbound
  server span - proving the correlation_id-tagged trace genuinely spans the gateway->api hop, not
  just one service's own request handling.
- Re-ran the same search **through Grafana's own datasource proxy** (`/api/datasources/proxy/uid/
  Tempo/api/search`) - the actual path a Grafana user's trace-explore view uses, not Tempo
  directly - and got the same result, confirming the datasource is genuinely wired and queryable,
  not just present in `docker-compose.yml`.
- Confirmed Prometheus's scrape targets (Day 27) remained healthy after adding the new
  `Platform.Observability` dependency and service wiring - no regression to the existing metrics
  pipeline.
- Full-solution `dotnet build`/`dotnet format --verify-no-changes`/`dotnet test`, all clean with
  unpiped exit codes (0/0/0). `build/scripts/smoke-test.sh` (the actual CI script) re-run clean,
  Postgres/RabbitMQ round-trip checks (Days 18/24) unaffected.

### Real issue found and fixed during verification (environmental, not a design bug)

The same class of stray-Docker-volume issue documented on Days 26/27 recurred once more on the
first fresh `docker compose up` of this day: RabbitMQ crashed with the same
`.erlang.cookie: eacces` error. This time root-caused to a leftover **anonymous volume** (not an
orphaned container, unlike Day 27's finding) from a prior interrupted cycle on this machine,
colliding with the volume Docker allocated for the fresh `rabbitmq` container. Identified via
`docker ps -a` / `docker volume ls` inspection (the stray volume had no attached container),
removed explicitly (`docker volume rm`), and RabbitMQ started healthy on the very next `docker
compose up` with no further changes. Documented as the same category of machine-specific leftover
state as Days 26/27, not a `docker-compose.yml` issue - the fix pattern (inspect, don't blindly
prune) is now established across three consecutive days.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Jaeger | Very common, purpose-built tracing UI | A second, separate query surface from Grafana - fragments the observability story Days 26/27 already unified in one place | User declined |
| No `correlation_id` span enrichment (rely on OTel's own trace/span IDs only) | Simpler exporter setup | Loses the ability to pivot from an existing Loki log line (keyed on `CorrelationId`) to its trace without a separate trace-ID-to-correlation-ID mapping step | Rejected - the whole point of adding tracing alongside two already-correlation-ID-keyed signals is to make all three cross-referenceable on the same key |

## Consequences

- `Platform.Observability` now depends on `Platform.Logging` (for `CorrelationIdMiddleware
  .HeaderName`) - a new, deliberate cross-Platform-library reference, not a layering violation
  (both are Platform-tier libraries; Observability enriching spans with a Logging-owned constant is
  the same category of dependency as Persistence/Messaging already have on Common).
- No dedicated unit test project added for the new `AddCustomsClearanceTracing` method - same
  judgment as Day 27's `AddCustomsClearanceMetrics`: thin OpenTelemetry configuration wiring with
  no branching logic worth unit-testing in isolation; correctness proven by the live multi-span,
  cross-service trace captured above.
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.9.0 carries a `NU1902` moderate-severity
  advisory (`GHSA-4625-4j76-fww9`, CVE-2026-42191) - a local-blob-injection risk in the OTLP
  exporter's **disk retry** feature, which only activates when the environment variable
  `OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY=disk` is explicitly set. This project never sets that
  variable (no disk-retry configuration anywhere in `Program.cs`/`docker-compose.yml`), so the
  advisory is not exploitable in this configuration. Left un-upgraded rather than force-bumping to
  a newer major version that could break compatibility with the pinned `OpenTelemetry.Exporter
  .Prometheus.AspNetCore` 1.9.0-beta.2 (Day 27) - revisit if a matching 1.9.x patch is published,
  or when the Prometheus exporter's own beta status is revisited.
- Tracing joins metrics/logs as the third and final signal of the "three pillars of observability"
  for this stack - Day 29 (Alerting) and Day 30 (mandatory sequential review) are the remaining
  Week 6 items.
- `/metrics`-style unauthenticated exposure reasoning (Day 27) does not apply here - Tempo's HTTP
  API (3200) is reachable only inside the compose network plus the published host port for local
  dev convenience, same posture as Prometheus's own 9090.

## Related

[[ADR-0021]] (Day 27 - the `Platform.Observability` library and Grafana provisioning pattern this
ADR extends with a tracing method and a third datasource), [[ADR-0020]] (Day 26 - the
`X-Correlation-Id`/`CorrelationIdMiddleware` mechanism this ADR reuses to tag spans), [[ADR-0016]]
(Day 22 Gateway - the gateway->api proxy hop that makes a trace's multi-span shape meaningful),
Day-Wise Plan Week 6 (Days 26-29, explicitly parallelizable, merged on Day 30).
