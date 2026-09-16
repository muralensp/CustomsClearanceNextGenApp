# Day 30 Execution Log — Observability Review

Phase 0, Week 6 (Observability), Day 30 of the Day-Wise Plan - the mandatory sequential
wiring/review step across Days 26-29. Deliverable: validate logs, metrics, traces, and alerts all
correlate for one sample request/event; produce an observability sign-off note.

---

## Step 1 — Re-read the original Day 28 scope before reviewing

Before testing, re-checked the Day-Wise Plan's own wording for Day 28: "Add correlation-ID-based
tracing across **gateway -> service -> event bus**." Day 28's actual implementation
(`AddCustomsClearanceTracing`) only covered ASP.NET Core + HttpClient instrumentation - the event
bus (RabbitMQ, Day 24) was never instrumented. Flagged as a real gap to investigate, not assumed
fixed.

## Step 2 — Fix the gap: trace context propagation across RabbitMQ

- New `src/Platform/Messaging/MessagingTelemetry.cs` - a shared `ActivitySource`
  (`"CustomsClearance.Platform.Messaging"`), using .NET's built-in `Activity`/W3C trace-ID format
  rather than adding a new `OpenTelemetry.Api` dependency to `Platform.Messaging`.
- `RabbitMqEventBus.PublishAsync` - starts a Producer-kind Activity, writes its `traceparent` into
  a new RabbitMQ message header (AMQP has no built-in trace-context carrier).
- `RabbitMqEventConsumer` - reads that header back, starts a Consumer-kind Activity parented to it,
  so the consumer span becomes a real child of the publisher's span.
- `Platform.Observability.AddCustomsClearanceTracing` - added `.AddSource(MessagingTelemetry
  .ActivitySourceName)`, without which the OpenTelemetry SDK silently ignores the new spans (an
  unregistered `ActivitySource` is a no-op). New `Platform.Observability` -> `Platform.Messaging`
  `ProjectReference`; `src/Gateway/Dockerfile` gained a `COPY` step for `Platform.Messaging` (the
  Gateway didn't previously build against it at all).

## Step 3 — Build/format/test clean before live verification

`dotnet build`/`dotnet format --verify-no-changes`/`dotnet test`, all unpiped exit code 0.
`Platform.Messaging`'s ADR-0007 coverage gate (80% CRUD-tier threshold) held at 94.56% line / 80%
branch after the new tracing code - no new test project needed, same judgment as the existing
`DomainEventDispatcher` coverage.

## Step 4 — Prove the deliverable end-to-end: one request that produces one event

Fresh `docker compose down -v` / `up -d --build` - all 9 containers up, no RabbitMQ startup-race
recurrence this time. Chose `POST /diagnostics/widgets` (not a bare `/health` call) specifically
because it logs, gets scraped as a metric, produces a domain event over RabbitMQ, and can be traced
- the one request that genuinely exercises all four signals at once, tagged with one known
`X-Correlation-Id`.

1. **Logs (Loki)**: queried by `CorrelationId` - got back both "Published event" and "HANDLED
   event" log lines from `api`, same correlation key across the whole flow.
2. **Traces (Tempo)**: queried by the same value as a `correlation_id` span tag - got back **one
   trace with five spans**: Gateway inbound -> Gateway outbound (proxy) -> Api inbound -> `Diagnostics
   WidgetCreated publish` (producer) -> `DiagnosticsWidgetCreated receive` (consumer). The event-bus
   hop is now genuinely part of the trace - the Step 2 fix, proven live.
3. **Metrics (Prometheus)**: queried `http_server_request_duration_seconds_count{http_route="/
   diagnostics/widgets"}` - confirmed the real request was counted.
4. **Alerts (Grafana)**: re-queried both Day 29 rules mid-test - both remained `inactive`/Normal,
   `health: ok`, confirming normal traffic doesn't spuriously trip them.

All four signals cross-referenced by the same correlation key for the same one request/event - the
literal deliverable.

## Step 5 — Final clean-up verification

`docker compose down -v`, then `build/scripts/smoke-test.sh` (the actual CI script) - unpiped exit
code 0, Postgres/RabbitMQ round-trip checks (Days 18/24) unaffected.

## Outcome

**Deliverable met, with one real gap found and fixed, not just confirmed clean**: logs, metrics,
traces, and alerts all demonstrably correlate for one real sample request that also produced one
real sample event - including the event-bus hop, which Day 28's original implementation had missed
against its own stated scope. **Decision recorded**: `adr/0024-observability-review.md` (Accepted).

**Status**: Complete. Days 26-30 (Week 6, Observability) are now fully complete as a
cross-validated set. Per the Day-Wise Plan, **Week 7 (Days 31-35, Rules Engine Skeleton)** is next
- explicitly marked non-parallelizable (a strict build chain), a different working shape from Week
6. Next unstarted item is **Day 31 — Rules Engine: Storage Model**, pending explicit instruction to
begin.
