# ADR-0040: RabbitMQ Publisher Reuses One Channel

**Status:** Accepted
**Date:** 2026-10-19
**Deciders:** Product Owner (Day 45 scope), Claude

## Context

The Day 42 baseline (`docs/performance/BASELINE-2026-10-19.md`) showed `POST /diagnostics/widgets` (a PostgreSQL
write plus one RabbitMQ publish) no longer scaling past about 10 virtual users, with p99 around 2 s. The cause was
left open. `RabbitMqEventBus` opened a channel, declared the exchange and closed the channel on every publish: three
synchronous broker round trips inside the request. Its own class comment (Day 24) said pooling channels was "a later
optimization if publish volume ever makes this a bottleneck".

## Evidence that the publish step was the cost

Tempo traces from a write-only run at 25 users (`docs/performance/WRITE-PATH-2026-10-19.md`): in the four slowest
requests (about 2.7 s) roughly 2.6 s sat inside the `publish` span; in normal requests the publish was about 4.6 ms
of a 10 ms request even unloaded. The reason for the uniform multi-second stalls was **not** established.

## Decision

`RabbitMqEventBus` keeps ONE long-lived publishing channel, declared once, guarded by a lock (a channel is not safe
for concurrent publishes; `BasicPublish` without confirms is a local buffer write, so the lock is held very briefly).
The channel is created lazily and recreated if the broker closes it; a channel that throws is discarded so the next
publish opens a fresh one. Message properties are unchanged (persistent, same headers, `traceparent` for tracing).
No publisher confirms were added: that is a delivery-guarantee decision, not a performance one, and behaviour is
the same as before.

## Result (alternating runs, write path only, same machine)

| | 10 users: rps, p99, max | 25 users: rps, p99, max |
|---|---|---|
| Original (3 runs, rps) | 136, 93, 127 | 145, 120, 157 |
| Original, last run | 127 rps, 671 ms, 984 ms | 157 rps, 1,663 ms, 2,812 ms |
| Fixed, run 1 | 164 rps, 159 ms, 239 ms | 337 rps, 184 ms, 342 ms |
| Fixed, run 2 | 217 rps, 126 ms, 231 ms | 292 rps, 220 ms, 371 ms |

About twice the throughput at 25 users and the multi-second tail is gone. Run-to-run variation is at least about 25%
(the three original runs differ that much), so only the large effect is trustworthy, not small differences.

## Consequences

- Recovery from a broker restart was tested live under traffic, old code and new: both returned 500s for about 35 s
  while RabbitMQ restarted (new: 59 of 150 requests, old: 61) and then recovered without restarting the Api.
- Publishes are now serialised through one channel. At the measured rates this is not a limit, but a much higher
  rate might want a small channel pool; not needed now.
- `RabbitMqEventBus` remains excluded from the coverage gate (real broker I/O, ADR-0007/0009); it is proven by the
  live checks above, the smoke test's publish-to-consume check and the E2E script.
- Still not established: why the old per-publish channel produced stalls of a uniform ~2.6 s.

## Alternatives considered

- **A channel pool.** More code and state for a rate we are nowhere near; one channel measured well enough.
- **Publisher confirms on a shared channel.** Changes delivery guarantees; out of scope for a performance fix.
- **Leave it.** The tail latency was the largest finding of the baseline.

## Related

ADR-0018 (event bus), ADR-0024 (tracing across the bus), ADR-0037 (the baseline), ADR-0007/0009 (coverage exclusion).
