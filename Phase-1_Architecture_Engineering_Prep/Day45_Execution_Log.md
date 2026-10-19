# Day 45 Execution Log - Buffer / Catch-up

Day 45 is the plan's buffer day. Scope chosen by the Product Owner: four items, in this order, each through its own
pull request (branch protection is on): (1) write-path investigation, (2) low-risk Dependabot batch,
(3) security items needing no new design, (4) India tariff-sourcing research.

## 1. Write-path investigation (this PR) - ADR-0040

**Question**: why does `POST /diagnostics/widgets` stop scaling past ~10 users (Day 42 baseline)?

**Method and finding**: write-only reruns, then Tempo traces of the fastest and slowest requests. In the four slowest
(~2.7 s) about 2.6 s was inside the RabbitMQ `publish` span; in normal requests the publish was ~4.6 ms of a 10 ms
request. The publisher opened a channel, declared the exchange and closed the channel on every publish.

**Change**: `RabbitMqEventBus` reuses one long-lived channel (lock-guarded, recreated if closed, discarded on error).
Behaviour otherwise unchanged (persistent messages, same headers, no confirms added).

**Measured** (alternating runs, same machine): 25 users 157 rps / p99 1,663 ms / max 2,812 ms (original) versus
337 and 292 rps / p99 184 and 220 ms / max 342 and 371 ms (fixed). Original runs varied 93-157 rps, so run-to-run
variation is about 25-30%; only the large effect is trusted. Full numbers: `docs/performance/WRITE-PATH-2026-10-19.md`.

**Resilience** (broker restarted under live write traffic, old vs new code): both returned 500s for ~35 s
(59 and 61 of 150 requests) and recovered without restarting the Api.

**Found on the way**: a perf run failed its first login (HTTP 500) because `docker compose run k6` re-created
PostgreSQL and RabbitMQ under the running Api. Not reproducible in isolation, cause unknown; the script now uses
`--no-deps` and waits for a working login.

**Not established**: why the old pattern stalled for a uniform ~2.6 s; the PostgreSQL share of the remaining latency
(EF Core is not traced); behaviour above 25 users with the fix. One event of 91 published had not been logged as
handled when the resilience logs were read a few seconds after the last write (probably in flight, not proven).
