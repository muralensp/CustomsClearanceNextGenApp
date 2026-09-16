# ADR-0024: Observability Review (Day 30)

**Status:** Accepted
**Date:** 2026-09-16
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 6 (Observability), Day 30 of the Day-Wise Plan - the mandatory sequential review step
that ties together Days 26-29 (structured logging, metrics, tracing, alerting), which were built
largely in parallel. Deliverable per the Day-Wise Plan: "Validate logs, metrics, traces, alerts all
correlate for one sample request/event" -> an observability sign-off note. Notably, the plan's own
Day 28 scope named "correlation-ID-based tracing across **gateway -> service -> event bus**" - the
event bus explicitly in scope for tracing, not just the HTTP hop.

## Finding: tracing did not actually cover the event bus

Reviewing Day 28's implementation against that original scope surfaced a real gap:
`AddCustomsClearanceTracing` (Day 28) only instrumented ASP.NET Core (inbound) and HttpClient
(outbound) - it never touched `RabbitMqEventBus`/`RabbitMqEventConsumer` (Day 24). A request that
triggered a domain event (e.g. `POST /diagnostics/widgets`, which publishes
`DiagnosticsWidgetCreated`) would show a trace for the HTTP hop only - the event's publish and
handling fell out of the trace entirely, even though Loki (Day 26) already correlated the same
request's HTTP and event-handling log lines via `CorrelationId`. This is exactly the shape of gap
Day 30 exists to catch: Days 26-29 were built in parallel against overlapping but not
cross-validated scope, and only a single reviewer looking at "does everything correlate for one
real request" would surface it.

## Decision

**Fixed as part of this review**, not deferred, since Day 30 is explicitly the "mandatory
sequential wiring" step, not a read-only audit:

1. **New `MessagingTelemetry` class** (`src/Platform/Messaging/MessagingTelemetry.cs`) - a shared
   `ActivitySource` (`"CustomsClearance.Platform.Messaging"`). Uses .NET's built-in
   `System.Diagnostics.Activity` (W3C trace-ID format by default) rather than taking a new
   `OpenTelemetry.Api` package dependency in `Platform.Messaging` - the existing
   `Microsoft.Extensions.*` dependency footprint there was deliberately left untouched.
2. **`RabbitMqEventBus.PublishAsync`** now starts a `Producer`-kind Activity around each publish,
   tagged with `messaging.system`/`messaging.destination`/`correlation_id`, and writes the
   Activity's W3C `traceparent` string into a new RabbitMQ message header - AMQP has no built-in
   trace-context carrier the way HTTP headers do, so this is the wire mechanism that lets the
   consumer link back to the same trace.
3. **`RabbitMqEventConsumer`** reads that `traceparent` header back off each received message and
   starts a `Consumer`-kind Activity with it as the parent ID, so the consumer span becomes a real
   child of the publisher's span - one connected trace, not two disconnected ones.
4. **`Platform.Observability.AddCustomsClearanceTracing`** gained `.AddSource(MessagingTelemetry
   .ActivitySourceName)` - without this, the OpenTelemetry SDK ignores a custom `ActivitySource`
   entirely (a source with no registered listener is a no-op), so the spans above would never
   reach Tempo even though the code creates them. Registered on both `Api` and `Gateway` (harmless
   on the Gateway, which never publishes/consumes events - an unused source, no spans emitted).
   This required a new `Platform.Observability` -> `Platform.Messaging` `ProjectReference`, and a
   corresponding `COPY` addition to `src/Gateway/Dockerfile` (the Gateway didn't previously build
   against `Platform.Messaging` at all).

## Verification

Full `docker compose down -v` / `up -d --build`, then **one real sample request that also produces
one real sample event** - `POST /diagnostics/widgets` (chosen deliberately over a bare `/health`
call, which logs nothing and publishes no event, and so cannot exercise all four signals at once):

- **Logs (Loki)**: queried by the request's `CorrelationId` - got back both the "Published event"
  and "HANDLED event" log lines from the `api` service, confirming request and event-handling logs
  share the same correlation key (already true since Day 26 - reconfirmed here, not assumed).
- **Traces (Tempo)**: queried by the same `CorrelationId` as a span tag - got back **one trace with
  five spans**: Gateway inbound (server) -> Gateway outbound (client, the proxy hop) -> Api inbound
  (server, `POST /diagnostics/widgets`) -> **`DiagnosticsWidgetCreated publish`
  (producer)** -> **`DiagnosticsWidgetCreated receive` (consumer)**. This is the fix from this
  review, proven live: the event bus hop is now genuinely part of the same trace.
- **Metrics (Prometheus)**: queried `http_server_request_duration_seconds_count` scoped to
  `http_route="/diagnostics/widgets"` - confirmed the real request was counted.
- **Alerts (Grafana)**: re-queried both Day 29 alert rules mid-test - both remained `inactive`/
  Normal with `health: ok`, confirming normal traffic doesn't spuriously trip them (the rules were
  already proven to fire correctly on a real failure in Day 29's own verification).

All four signals independently confirmed for the same one request/event, using its shared
correlation key as the cross-reference - the literal Day 30 deliverable. No RabbitMQ
startup-race recurrence this day (the Days 26-28 stray-volume/orphaned-container issue did not
reappear). Full-solution `dotnet build`/`dotnet format --verify-no-changes`/`dotnet test`, all
clean with unpiped exit codes (0/0/0); `Platform.Messaging`'s coverage gate (ADR-0007, 80%
CRUD-tier threshold) held at 94.56% line / 80% branch despite the new tracing code path (no new
test project needed - the tracing additions are thin instrumentation calls around already-tested
publish/consume logic, same judgment as `DomainEventDispatcher`'s existing test coverage). CI's
`build/scripts/smoke-test.sh` re-run clean, Postgres/RabbitMQ round-trip checks (Days 18/24)
unaffected.

## Consequences

- The Gateway now builds against `Platform.Messaging` (transitively, via `Platform.Observability`)
  despite never publishing or consuming events itself - a deliberate, documented cost of registering
  one shared tracing configuration method for both services rather than parameterizing which
  sources each service listens to. Revisit only if this dependency ever becomes a real problem
  (e.g. a future Gateway-side reason to avoid `RabbitMQ.Client` in its dependency tree).
- This is the first ADR in the series that documents a **finding-and-fix within the review day
  itself**, rather than a forward decision - consistent with the Day-Wise Plan's own framing of
  Day 30 as "wiring/review," not a passive audit.
- Days 26-30 (Observability, Week 6) are now complete as a whole, cross-validated set, not just
  five individually-complete days. Week 7 (Days 31-35, Rules Engine Skeleton) is explicitly marked
  "No" for parallelization in the Day-Wise Plan (a strict build chain) - the next phase of work
  looks structurally different from Week 6's parallel-then-merge shape.

## Related

[[ADR-0020]] (Day 26 - CorrelationId/Loki, the log-correlation half this review reconfirmed),
[[ADR-0021]] (Day 27 - Prometheus/metrics, reconfirmed unaffected), [[ADR-0022]] (Day 28 - the
tracing implementation this review found the event-bus gap in and fixed), [[ADR-0023]] (Day 29 -
the alert rules reconfirmed healthy against real traffic), [[ADR-0018]] (Day 24 - RabbitMqEventBus/
RabbitMqEventConsumer, the classes this review instrumented), Day-Wise Plan Week 6 (Days 26-30).
