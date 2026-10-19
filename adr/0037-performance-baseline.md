# ADR-0037: Performance Baseline - Load Tool, Scenarios and a Configurable Gateway Rate Limit

**Status:** Accepted
**Date:** 2026-10-19
**Deciders:** Product Owner (four design choices below), Claude (Day 42)

## Context

Plan wording (Day 42): "Run basic load test against gateway + sample service to establish a performance
baseline"; deliverable "Baseline performance report". `tests/Performance/` was empty and no load tool was
installed. The Gateway's rate limit was hard-coded at 100 requests per 10 s per client IP (Day 22), so any
load test from one machine would mostly receive HTTP 429 and measure the limiter, not the system.

## Decision

1. **The Gateway rate limit becomes configuration.** `RateLimiting:PermitLimit` and `RateLimiting:WindowSeconds`
   (`RateLimitingOptions`, validated at start-up; zero or negative fails fast). Defaults stay 100 and 10, so
   with no configuration behaviour is unchanged. `docker-compose.yml` passes `GATEWAY_RATE_LIMIT_PERMITS` and
   `GATEWAY_RATE_LIMIT_WINDOW_SECONDS` (defaults 100 and 10). Only the load-test run raises them.
2. **k6, run from Docker.** A `k6` service (`grafana/k6:0.54.0`) under the compose `perf` profile, so it never
   starts with a plain `docker compose up`. It joins the compose network and calls the Gateway by service name,
   avoiding Docker Desktop's host port forwarding. Nothing to install per developer.
3. **Three paths, four load levels.** `health` (proxy only), `read` (JWT + one PostgreSQL read), `write`
   (PostgreSQL write + RabbitMQ publish), each at 1, 10, 25 and 50 virtual users for 30 s, one level at a time.
   The rules-engine evaluation was left out for this first pass.
4. **On demand, not in CI.** `build/scripts/perf-baseline.sh` runs it; the report is committed
   (`docs/performance/BASELINE-2026-10-19.md`). Shared CI runners are too noisy for numbers anyone should
   compare against, and there are no targets yet to turn into a regression gate.

## Consequences

- The first baseline is in the report. Headline: no failures at any level; `health` and `read` scale smoothly;
  `write` stops scaling near 140-180 rps with p95 rising to about 735-795 ms at 25-50 users.
- The write-path cause is **not established**. The code opens and closes a RabbitMQ channel per publish and
  RabbitMQ showed the highest CPU; that is a hypothesis, recorded in the report as untested.
- The numbers are from one run on a laptop under Docker Desktop with everything on one host. They are only
  comparable with runs on the same machine, and run-to-run variation is unknown.
- Production limits for the Gateway are still undecided; this only makes them settable without a code change.
- k6 thresholds are deliberately trivial (they exist so k6 reports per-level sub-metrics). A real regression
  gate needs targets from the PRD (Ch. 1.3, Ch. 3.5) that no built feature maps onto yet.

## Alternatives considered

- **Leave the limiter hard-coded and test the Api directly** - measures the Api but not the Gateway under load.
- **Stay under the limit (10 req/s)** - gives latency but no capacity information.
- **A custom .NET load generator** - one more tool and percentile implementation to maintain.
- **A CI job** - noisy numbers, added CI minutes, nothing to gate on yet.

## Related

ADR-0016 (Gateway), ADR-0035 (slot isolation; the runner honours slot variables), ADR-0037 is referenced by
`docs/performance/BASELINE-2026-10-19.md`, `tests/Performance/baseline.js`, `build/scripts/perf-baseline.sh`.
