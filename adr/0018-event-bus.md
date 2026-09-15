# ADR-0018: Event Bus (Day 24)

**Status:** Accepted
**Date:** 2026-09-15
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Day 24 of the Day-Wise Plan ("Event Bus") calls for: stand up an event bus (Kafka/
RabbitMQ/Azure Service Bus); publish/consume a test domain event. Deliverable: "Event bus
operational with sample event flow."

ADR-0012 (Day 18) already anticipated this exact day: `InMemoryEventBus` was explicitly built as
"a functional, same-process placeholder... Swapping to Kafka/RabbitMQ/Azure Service Bus later (a
Phase 0 infrastructure decision) means providing a new `IEventPublisher` implementation - the
envelope shape and `IEventHandler` contract used by application code do not change." Day 24 is
that anticipated swap.

Unlike Days 21/22, a message broker doesn't have the same Modular Monolith (ADR-0003) tension the
Gateway did - a broker container is normal infrastructure regardless of monolith-vs-microservices,
so the only real decisions were broker technology and wiring scope, both confirmed with the user:

1. **RabbitMQ** over Kafka (heavier operational footprint - broker + KRaft/ZooKeeper - for zero
   real event volume yet) or Azure Service Bus (requires the cloud/tenant commitment this project
   has deliberately deferred twice already, ADR-0008/ADR-0015). Consistent with this project's
   repeated pattern of picking the lower-footprint option for solo-scale dev infra (Docker Compose
   over Kubernetes, YARP over Ocelot, in-house Identity over a managed provider).
2. **Wire it in behind `AddCustomsClearanceMessaging()`**, not just stand up an unwired container -
   a real `IEventPublisher` implementation, config-driven (same "absence means skip" convention as
   `ConnectionStrings:DiagnosticsDb`/`IdentityDb`), with a real publish→consume round trip proven
   end to end - fulfilling the literal deliverable, not just "the container starts."

## Decision

1. **`RabbitMqOptions`** bound from a `"RabbitMq"` config section; `HostName` presence is this
   library's signal to wire the real broker instead of `InMemoryEventBus` (Program.cs checks it
   the same way it already checks `DiagnosticsDb`/`IdentityDb` connection strings).
2. **`RabbitMqEventBus : IEventPublisher`** - publishes the whole `DomainEventEnvelope<TPayload>`
   as JSON to a durable topic exchange (`customsclearance.domain-events`), routed by `EventType`,
   so a future second consumer could subscribe to a subset of event types without this publisher
   changing. One shared `IConnection`, a new `IModel` (channel) per publish call - the
   documented-safe default for RabbitMQ.Client, since `IModel` isn't thread-safe to share across
   concurrent calls.
3. **`RabbitMqEventConsumer : BackgroundService`** - binds this host's queue
   (`customsclearance.domain-events.api`) to the exchange with a catch-all (`#`) routing key,
   manually acks only after successful handling (an exception nacks with requeue, giving RabbitMQ's
   own redelivery a chance). No dead-letter queue yet - Day 24 scope is proving one sample flow,
   not full delivery-failure semantics.
4. **`EventTypeRegistry` + `DomainEventDispatcher`** - the real design problem Day 24 introduces:
   `InMemoryEventBus` dispatches to `IEventHandler<TPayload>` with `TPayload` known at the publish
   call site (compile-time generic). A RabbitMQ consumer receives only wire bytes - `TPayload`
   isn't known until the message arrives. `EventTypeRegistry` maps `EventType` (string) → CLR
   payload type (the Api host's composition root registers this, same responsibility as
   registering its own `IEventHandler<T>` implementations); `DomainEventDispatcher` uses that
   mapping plus reflection (`MakeGenericMethod`-free here, but `MakeGenericType` +
   `MethodInfo.Invoke`) to deserialize into the right `DomainEventEnvelope<TPayload>` and invoke
   every matching handler - functionally identical dispatch to `InMemoryEventBus`, just
   reflection-driven instead of compile-time-generic.
5. **Coverage split, deliberately**: `DomainEventDispatcher` (the highest-risk part - generic type
   resolution + reflection invocation) is fully unit-tested without a real broker.
   `RabbitMqEventBus`/`RabbitMqEventConsumer` (real network I/O to RabbitMQ) are marked
   `[ExcludeFromCodeCoverage]` with an explicit justification, rather than force-covered with
   hand-rolled fakes for `RabbitMQ.Client`'s `IConnection`/`IModel` surface - this codebase has no
   mocking library anywhere (by pattern, not oversight: see `EfRepository`'s real-Postgres-only
   proof approach), and hand-writing that fake surface wasn't proportionate to Day 24's scope. Both
   are instead proven via a real `docker compose` run and, from Day 24 on, a CI-enforced check in
   `build/scripts/smoke-test.sh`.

### Real bug found and fixed during verification

**Startup race**, the same class of bug as Day 18's Postgres finding: `docker-compose`'s
`rabbitmq` healthcheck (`rabbitmq-diagnostics -q ping`) reports healthy once the Erlang node is
up, slightly before the AMQP listener (port 5672) actually accepts connections. The Api crashed at
startup with `RabbitMQ.Client.Exceptions.BrokerUnreachableException` (`Connection refused`)
hitting exactly that gap - confirmed via a real `docker compose up` run, not assumed from the
healthcheck's presence. Fixed with `RabbitMqConnectionHelper.CreateConnectionWithRetry` (10
attempts, 2s backoff, synchronous since both call sites are inside eagerly-resolved constructors
where an async retry loop isn't available) - the same retry-with-backoff shape as Program.cs's
existing `DiagnosticsDb`/`IdentityDb` startup retries, adapted to a sync constructor context.
Re-verified from a fresh `docker compose down -v` / `up --build` cycle: 8 retries, then a clean
startup.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Kafka | Industry-standard for high-throughput streaming, closer to a 500,000-shipments/day eventual target | Meaningfully heavier operational footprint (broker + KRaft/ZooKeeper) for a solo dev environment with zero real event volume | User declined |
| Azure Service Bus | Managed, production-grade | Requires an actual Azure account/tenant, deferred twice already | User declined |
| Stand up the container only, no code wiring | Smaller change, zero risk to existing `AddCustomsClearanceMessaging()` call sites | Doesn't satisfy "publish/consume a test domain event" as a proven deliverable - just proves a container starts | User declined |
| Hand-write `IConnection`/`IModel` fakes to unit-test `RabbitMqEventBus`/`RabbitMqEventConsumer` directly | Would give 100% unit coverage including the I/O classes | Large interface surfaces to fake correctly for two classes whose real risk (reflection dispatch) is already isolated and tested elsewhere; introduces a testing pattern (hand-rolled fakes for a third-party client) this codebase has avoided everywhere else | Rejected - extract-and-test the real risk (`DomainEventDispatcher`) instead, exclude the I/O boundary with a clear justification, same proportionality judgment as every prior "what's worth testing here" decision in this codebase |

## Consequences

- `build/scripts/smoke-test.sh` (and therefore CI) now asserts a real RabbitMQ publish→consume
  round trip, not just that the broker container starts - the same EventId appears in both the
  publish confirmation and the handler's "HANDLED event" log line, checked via `docker compose
  logs api`.
- Any future bounded context that wants a real (not in-memory) event flow registers its payload
  type with `EventTypeRegistry` in the Api host's composition root - a small, explicit
  registration step, not automatic discovery; worth documenting if it becomes a repeated point of
  friction once more contexts are real.
- No dead-letter queue, no retry-count limit on nack-requeue (an unprocessable message could loop
  indefinitely) - explicitly flagged as Day 24 scope, not forgotten; revisit once real event volume
  or a real failure mode is observed.
- The `RabbitMqConnectionHelper` retry pattern (sync, constructor-context) is now the second
  instance of "container healthcheck passes before the service is actually ready" in this codebase
  (Postgres, Day 18; RabbitMQ, Day 24) - worth checking for on any future broker/datastore
  container added to `docker-compose.yml`.

## Related

[[ADR-0012]] (Persistence & Messaging - the `IEventPublisher`/`IEventHandler<T>`/
`DomainEventEnvelope<TPayload>` contract this ADR provides a real implementation of, and the swap
this ADR explicitly anticipated), [[ADR-0009]] (Test layering - why `RabbitMqEventBus`/
`RabbitMqEventConsumer` are proven at the container layer, not unit tests), [[ADR-0015]] / [[ADR-0016]]
/ [[ADR-0017]] (Days 21-23 - the pattern of confirming technology/wiring-scope decisions with the
user before building, continued here).
