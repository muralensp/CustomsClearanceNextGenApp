# ADR-0020: Structured Logging & Log Centralization (Day 26)

**Status:** Accepted
**Date:** 2026-09-16
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 6 (Observability), Day 26 of the Day-Wise Plan ("Structured Logging Rollout") calls
for: wire `logging-lib` into all scaffold services; centralize log output. Deliverable:
"Centralized log view (e.g. ELK/Loki) showing service logs."

`Platform.Logging`'s `CorrelationIdMiddleware` (Day 16) was already wired into both running
services (`Api`, Day 16; `Gateway`, Day 22/25), so "wire logging-lib into all scaffold services"
was largely already satisfied. The real remaining gap was **centralization**: both services still
emitted plain-text console output only, and nothing aggregated it anywhere.

Two decisions were confirmed with the user before building:

1. **Grafana Loki + Promtail + Grafana**, over ELK (Elasticsearch's memory footprint is
   meaningfully heavier for a solo dev environment with low log volume) - consistent with this
   project's repeated pattern of choosing the lower-footprint option (Docker Compose over
   Kubernetes, RabbitMQ over Kafka, YARP over Ocelot, in-house Identity over a managed provider).
2. **Structured JSON console output via .NET's built-in JSON formatter**
   (`Microsoft.Extensions.Logging.Console`'s `AddJsonConsole`), over adopting Serilog - no new
   dependency, and this codebase hasn't used Serilog (or any third-party logging framework)
   anywhere; the built-in formatter already captures the `CorrelationId` scope
   `CorrelationIdMiddleware` sets, which is the field that actually matters for a "centralized"
   view to be useful (tracing one request/event across services).

## Decision

1. **`Platform.Logging.LoggingBuilderExtensions.AddCustomsClearanceStructuredLogging()`** - a new
   shared extension (matching the `AddCustomsClearance*` convention every other cross-cutting
   library uses), called once each by `Api`'s and `Gateway`'s `Program.cs`. Clears the default
   Console/Debug/EventSource providers `WebApplication.CreateBuilder` wires in and adds the
   built-in JSON console formatter instead - existing `LogLevel` filtering from each service's
   `"Logging"` config section is unaffected (that's separate configuration binding).
2. **`docker-compose.yml`** gains three new services:
   - `loki` (`grafana/loki:2.9.8`) - the log store/query engine, using the image's baked-in
     `local-config.yaml` (no custom config needed for this scope).
   - `promtail` (`grafana/promtail:2.9.8`) - scrapes every container's `json-file` log driver
     output via Docker service discovery (`observability/promtail-config.yaml`), relabeling by
     `com.docker.compose.service` so queries can filter with `{compose_service="api"}` rather than
     the generated container hash name. Requires read-only mounts of the Docker socket and
     `/var/lib/docker/containers` - standard Promtail-on-Docker-Compose pattern, works identically
     under Docker Desktop's Linux VM as on a native Linux host.
   - `grafana` (`grafana/grafana:11.1.0`) - the query UI, with Loki **pre-provisioned as a
     datasource** (`observability/grafana-datasources.yaml`) so the "centralized log view" is
     reproducible via `docker compose up` alone, not a manual point-and-click setup step every
     time the stack is rebuilt. Anonymous admin access is a dev-only convenience (no login
     screen), the same posture as every other `LOCAL-DEV-ONLY` placeholder already in this stack -
     never a real security decision, and not something to carry into a real environment.

### Real issue found and fixed during verification (environmental, not a design bug)

A fresh `docker compose down -v` / `up --build` cycle failed with RabbitMQ crashing at startup
(`Error when reading /var/lib/rabbitmq/.erlang.cookie: eacces`) - traced to a stray anonymous
Docker volume left over from an earlier interrupted run, not anything this ADR's changes touched.
Fixed with `docker volume prune`; re-verified from a genuinely clean state afterward. Documented
here because it briefly looked like a Day 26 regression before being isolated.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| ELK (Elasticsearch + Logstash + Kibana) | More "enterprise-standard," full-text search | Elasticsearch's memory footprint is substantial for a solo dev environment with near-zero log volume; Logstash adds a third moving part Loki's design doesn't need (Promtail ships directly to Loki) | User declined |
| Serilog (with a Loki or Elasticsearch sink) | Richer enrichment/structured-logging ergonomics than the built-in formatter | New major dependency and logging pattern this codebase has used nowhere else; the built-in JSON formatter already captures everything Day 26 needs (the `CorrelationId` scope) | User declined |
| Ship logs to Loki via a direct application-level Loki client/sink instead of Promtail scraping container stdout | Avoids mounting the Docker socket into a container | Couples every service directly to Loki's API (a new dependency per service, and a new failure mode - what happens to app logs if the sink is unreachable?); Promtail scraping stdout is transport-agnostic and matches "containers log to stdout, infrastructure handles shipping," which this project's existing `docker logs` habits already assume | Rejected - scraping keeps application code free of log-shipping concerns entirely |

## Consequences

- Any future bounded context automatically gets centralized, structured logs the moment it's
  containerized as part of this `docker-compose.yml` - no per-service Promtail configuration
  needed (the scrape config is Docker-service-discovery-based, not a static per-container list).
- Raw `docker logs <container>` output is now JSON, not human-formatted text - a minor
  local-debugging ergonomics cost, accepted because it's what makes centralization possible without
  a format-translation step; Grafana's Explore view is the intended way to read logs going
  forward, not raw `docker logs`.
- Anonymous Grafana access must be revisited before any non-dev environment exists (still
  deferred, per ADR-0008/0015's hosting-decision framing) - flagged now so it isn't carried forward
  by accident.
- No log retention/rotation policy configured yet (Loki's defaults) - fine for dev-time volume,
  worth revisiting once real usage exists.

## Related

[[ADR-0010]] (Logging & Error Handling libraries - `Platform.Logging`'s original `CorrelationIdMiddleware`
this ADR builds on), [[ADR-0016]] (Day 22 Gateway - the second service this ADR's structured
logging now covers), [[ADR-0019]] (Day 25 Base Security Framework - the immediately preceding
Week 5 day), Day-Wise Plan Week 6 (Days 26-30, Observability - Day 26 is the first of five,
explicitly marked "Partial - parallel with 27-29, merge on Day 30").
