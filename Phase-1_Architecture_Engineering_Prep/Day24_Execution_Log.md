# Day 24 Execution Log — Event Bus

Phase 0, Week 5 (Core Infrastructure), Day 24 of the Day-Wise Plan. Deliverable: "Event bus
operational with sample event flow."

---

## Step 1 — Confirm broker choice and wiring scope before building

Asked the user directly rather than assuming, since Day 24 didn't have an obvious lower-footprint
default the way Days 21/22 did:
1. **RabbitMQ** over Kafka (heavier for zero real event volume) or Azure Service Bus (cloud
   commitment already deferred twice).
2. **Wire it in behind `AddCustomsClearanceMessaging()`**, proving a real publish→consume round
   trip, not just standing up an unwired container.

Recorded both, plus the reflection-dispatch design this required, as `adr/0018-event-bus.md`.

## Step 2 — Recognize the real design problem: TPayload isn't known at consume time

`InMemoryEventBus` (Day 18) dispatches to `IEventHandler<TPayload>` with `TPayload` known at the
publish call site (compile-time generic) - trivial to resolve from DI. A RabbitMQ consumer
receives only wire bytes; `TPayload` isn't known until the message's `EventType` routing key
arrives. Solved with `EventTypeRegistry` (maps `EventType` string → CLR payload type, registered
by the Api host's composition root) + `DomainEventDispatcher` (uses that mapping plus reflection
to deserialize into the right `DomainEventEnvelope<TPayload>` and invoke matching handlers) -
functionally identical dispatch to `InMemoryEventBus`, just reflection-driven.

## Step 3 — Build

- `src/Platform/Messaging/RabbitMqOptions.cs` - bound from `"RabbitMq"` config, `HostName`
  presence is the wire-vs-InMemory signal (same convention as `DiagnosticsDb`/`IdentityDb`).
- `EventTypeRegistry.cs` - `Register<TPayload>(eventType)` / `ResolvePayloadType(eventType)`.
- `DomainEventDispatcher.cs` - the reflection dispatch, deliberately extracted as its own class so
  it's unit-testable without a real broker (the highest-risk part of this day).
- `RabbitMqEventBus.cs` - real `IEventPublisher`, publishes JSON envelopes to a durable topic
  exchange routed by `EventType`.
- `RabbitMqEventConsumer.cs` - `BackgroundService`, binds a queue to the exchange
  (catch-all routing key), delegates to `DomainEventDispatcher`, manual ack/nack-with-requeue.
- `RabbitMqConnectionHelper.cs` - retry-with-backoff around connection creation (see Step 5).
- `ServiceCollectionExtensions.AddCustomsClearanceRabbitMqMessaging(...)` - registers all of the
  above, overriding `AddCustomsClearanceMessaging()`'s `InMemoryEventBus` registration.
- `docker-compose.yml` - `rabbitmq` service (management image, AMQP + management UI ports,
  `rabbitmq-diagnostics -q ping` healthcheck), `api` now `depends_on: rabbitmq: condition:
  service_healthy` and gets `RabbitMq__HostName=rabbitmq`.
- `Program.cs` - config-driven: wires `AddCustomsClearanceRabbitMqMessaging` (registering
  `DiagnosticsWidgetCreatedPayload` → `"DiagnosticsWidgetCreated"`) when `RabbitMq:HostName` is
  configured, else falls back to `AddCustomsClearanceMessaging()` (InMemory) - same pattern as the
  `DiagnosticsDb`/`IdentityDb` guards.
- `TestApiFactory.cs` - blanks `RabbitMq__HostName` too, so integration tests stay infrastructure-
  free per ADR-0009 (this was caught proactively this time, having been bitten by the same class
  of miss twice already on Days 18/23).

## Step 4 — Coverage: extract the real risk, exclude the I/O boundary

Adding `RabbitMqEventBus`/`RabbitMqEventConsumer` initially dropped `Platform.Messaging` to
21.91% line coverage, well under ADR-0007's 80% gate. Rather than force-cover real network I/O
with hand-rolled fakes for `RabbitMQ.Client`'s `IConnection`/`IModel` (large interface surfaces,
and this codebase has no mocking library anywhere, by consistent pattern), extracted
`DomainEventDispatcher` (already planned in Step 2, but its testability was the deciding factor in
keeping it separate) and unit-tested it directly: registered-event-type dispatch, multi-handler
fan-out, unregistered-event-type skip (no throw), no-handlers-registered (no throw). Added
`ServiceCollectionExtensionsTests` covering `AddCustomsClearanceRabbitMqMessaging`'s options
binding and service registration **without** resolving `IEventPublisher`/`RabbitMqEventConsumer`
themselves (doing so would open a real TCP connection inside a unit test). Marked
`RabbitMqEventBus`/`RabbitMqEventConsumer` `[ExcludeFromCodeCoverage]` with an explicit
justification citing the live docker-compose proof instead. Result: 95.6% line coverage, exit 0.

## Step 5 — Real bug found and fixed: RabbitMQ startup race

First live `docker compose up` run crashed the Api at startup:
`RabbitMQ.Client.Exceptions.BrokerUnreachableException` → `Connection refused`. Root cause: the
same class of bug as Day 18's Postgres finding - `docker-compose`'s `rabbitmq` healthcheck
(`rabbitmq-diagnostics -q ping`) reports healthy once the Erlang node is up, slightly before the
AMQP listener (port 5672) actually accepts connections. Fixed with
`RabbitMqConnectionHelper.CreateConnectionWithRetry` (10 attempts, 2s backoff) shared by both
`RabbitMqEventBus` and `RabbitMqEventConsumer`'s constructors - synchronous, since both are
resolved eagerly at host startup where an async constructor isn't available (same retry shape as
`Program.cs`'s existing `DiagnosticsDb`/`IdentityDb` loops, adapted to a sync context). Re-verified
from a genuinely fresh `docker compose down -v` / `up --build` cycle: 8 retries, then a clean
`Now listening on: http://[::]:8080`.

## Step 6 — Prove the deliverable end-to-end against real running containers

1. Fresh `docker compose down -v` + `up -d --build` - all 4 containers (`gateway`, `api`,
   `postgres`, `rabbitmq`) healthy.
2. `POST /diagnostics/widgets?name=day24-rabbitmq-check` (through the Day 22 gateway) → 200.
3. Api logs show, in order: `Published event DiagnosticsWidgetCreated ... to RabbitMQ exchange`,
   `Dispatching event DiagnosticsWidgetCreated to 1 handler(s)`, `HANDLED event
   DiagnosticsWidgetCreated (<same EventId>) for widget ...` - the **same EventId and
   CorrelationId** at publish and at handle, proving a genuine round trip through the real broker,
   not an in-process shortcut.
4. RabbitMQ management API (`GET /api/exchanges/%2F/customsclearance.domain-events`, `GET
   /api/queues/%2F/customsclearance.domain-events.api`) confirms the durable topic exchange and
   queue exist with an active consumer - the "operational" half of the deliverable, independently
   of the log-based proof.
5. `build/scripts/smoke-test.sh` (the actual CI script) extended with a RabbitMQ round-trip check:
   after the existing Postgres widget-creation call, polls `docker compose logs api` for a
   "HANDLED event" line carrying the same `eventId` the create call returned. Ran it for real
   (unpiped exit code 0): Postgres round-trip verified, then "Event ... handled after 1
   attempt(s)," RabbitMQ round-trip verified.
6. Full-solution `dotnet build`/`dotnet format --verify-no-changes`/`dotnet test`, all re-confirmed
   clean with **unpiped** exit codes (0/0/0) - a discipline explicitly carried over from Day 23's
   meta-finding about piped exit codes silently masking failures.

## Outcome

**Deliverable met**: RabbitMQ operational with a proven sample event flow - the same
`DiagnosticsWidgetCreated` event Day 18 proved in-process now round-trips through a real broker,
verified via server logs, the RabbitMQ management API, and a CI-enforced smoke-test check.
**One real bug found and fixed** (RabbitMQ startup race, the same class as Day 18's Postgres
finding - worth checking for on any future broker/datastore container). **Decision recorded**:
`adr/0018-event-bus.md` (Accepted).

**Status**: Complete. Next unstarted item is **Day 25 — Base Security Framework**, pending
explicit instruction to begin.
