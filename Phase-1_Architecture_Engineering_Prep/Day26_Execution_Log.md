# Day 26 Execution Log — Structured Logging Rollout

Phase 0, Week 6 (Observability), Day 26 of the Day-Wise Plan - first of five days marked "Partial
- parallel with 27-29, merge on Day 30." Deliverable: "Centralized log view (e.g. ELK/Loki)
showing service logs."

---

## Step 1 — Confirm stack and log-format choices before building

Asked the user directly rather than assuming, since both are real infra/dependency decisions:
1. **Grafana Loki + Promtail + Grafana** over ELK - lighter footprint, consistent with every prior
   infra choice in this project (Docker Compose, RabbitMQ, YARP, in-house Identity).
2. **Built-in JSON console formatter** over adopting Serilog - no new dependency, and this
   codebase has never used a third-party logging framework.

Recorded both as `adr/0020-structured-logging-and-log-centralization.md`.

## Step 2 — Recognize "wire logging-lib into all scaffold services" was mostly already done

`CorrelationIdMiddleware` (Day 16) was already wired into both running services (`Api`, Day 16;
`Gateway`, Day 22/25) - the real remaining gap was centralization, since both still emitted
plain-text console output with nothing aggregating it.

## Step 3 — Build

- `src/Platform/Logging/LoggingBuilderExtensions.cs` - new
  `AddCustomsClearanceStructuredLogging()` extension (matching the `AddCustomsClearance*`
  convention every other cross-cutting library uses), clears default console/debug/event-source
  providers and adds the built-in JSON console formatter.
- `src/Api/Program.cs` / `src/Gateway/Program.cs` - one line each,
  `builder.Logging.AddCustomsClearanceStructuredLogging()`.
- `observability/promtail-config.yaml` - Docker service-discovery scrape config, relabels by
  `com.docker.compose.service` for queryable `{compose_service="api"}`-style filters.
- `observability/grafana-datasources.yaml` - pre-provisions Loki as a Grafana datasource, so the
  log view is reproducible via `docker compose up` alone.
- `docker-compose.yml` - added `loki`, `promtail` (mounts Docker socket + container log
  directory, read-only), `grafana` (anonymous admin access, a dev-only convenience) services.

## Step 4 — Coverage

Added `tests/Unit/Platform.Logging.Tests/LoggingBuilderExtensionsTests.cs`: resolves the real
`ConsoleLoggerOptions` the extension configures and asserts the formatter name is
`ConsoleFormatterNames.Json` (not just "doesn't throw"), plus a smoke test that a logger built
through the extension actually logs without error. `Platform.Logging` coverage: 100%
line/branch/method (was 82.14% before this test, comfortably above the 80% gate either way, but
verified the real behavior directly rather than relying on incidental coverage).

## Step 5 — Environmental issue found during verification (not a Day 26 design bug)

First fresh `docker compose down -v` / `up --build` cycle failed: RabbitMQ crashed at startup
(`Error when reading /var/lib/rabbitmq/.erlang.cookie: eacces`). Traced to a stray anonymous
Docker volume left over from an earlier interrupted session, unrelated to anything this day's
changes touched (docker-compose only *added* new services; didn't modify the `rabbitmq` service
definition). Fixed with `docker volume prune -f`; re-verified from a genuinely clean state
afterward with no recurrence. Documented in `adr/0020` since it briefly looked like a regression
before being isolated.

## Step 6 — Prove the deliverable end-to-end against real running containers

1. Fresh `docker compose up -d --build` (after the volume cleanup) - all 7 containers
   (`gateway`, `api`, `postgres`, `rabbitmq`, `loki`, `promtail`, `grafana`) up.
2. `docker logs` on the `api` container confirmed real JSON output (structured fields: `EventId`,
   `LogLevel`, `Category`, `Message`, `State` with named properties like `CorrelationId`) -
   the format change itself proven, not assumed from the code.
3. Generated real traffic: `GET /health` and `POST /diagnostics/widgets` both tagged with a known
   `X-Correlation-Id: day26-loki-check-001` header.
4. **Queried Loki directly** (`GET /loki/api/v1/query_range?query={compose_service="api"} |=
   "day26-loki-check-001"`) - returned both the "Published event..." and "HANDLED event..." log
   lines, both carrying `CorrelationId=day26-loki-check-001` in their structured `State` - proving
   Promtail successfully shipped container logs into Loki and they're queryable by label and
   content.
5. **Confirmed Grafana's provisioning worked without manual setup**: `GET
   /api/datasources` showed the Loki datasource already present (`isDefault: true`), matching
   `observability/grafana-datasources.yaml` exactly - no click-through configuration needed.
6. **Queried through Grafana's own datasource proxy** (not just Loki directly) for
   `{compose_service="gateway"}` - returned real gateway log streams, proving the actual "view" a
   user would use (Grafana, not raw Loki API calls) works end to end.
7. Fresh `docker compose down -v` + `build/scripts/smoke-test.sh` (the actual CI script) re-run -
   unpiped exit code 0, Postgres and RabbitMQ round-trip checks (Days 18/24) both still pass,
   unaffected by the new logging format or the three new services.
8. Full-solution `dotnet build`/`dotnet format --verify-no-changes`/`dotnet test`, all clean with
   unpiped exit codes (0/0/0).

## Outcome

**Deliverable met**: a real, queryable centralized log view - Loki aggregating structured JSON
logs from every container via Promtail's Docker service discovery, Grafana pre-provisioned and
proven to serve real queries against it, with `CorrelationId` traceable across a request's full
publish→consume lifecycle (Api → RabbitMQ → handler) in one query. **One environmental issue
isolated and fixed** (stray volume, not a design bug). **Decision recorded**:
`adr/0020-structured-logging-and-log-centralization.md` (Accepted).

**Status**: Complete. Per the Day-Wise Plan, Days 26-29 (Structured Logging, Monitoring & Metrics,
Distributed Tracing, Alerting Basics) can be scaffolded in parallel, with Day 30 as the mandatory
sequential wiring/review step tying them together. Next unstarted item is **Day 27 — Monitoring &
Metrics**, pending explicit instruction to begin.
