# Write-path investigation - 2026-10-19 (Day 45, ADR-0040)

Follow-up to `BASELINE-2026-10-19.md`: why did `POST /diagnostics/widgets` stop scaling past about 10 users?

## Method

1. Re-ran the write path alone (10 and 25 users, 30 s each) through `build/scripts/perf-baseline.sh` with
   `PERF_PATHS=write`, and kept the stack up.
2. Queried Tempo for the traces from that run and compared the spans of the fastest and slowest requests.
3. Changed one thing (the publish channel), then ran original, fixed, original-vs-fixed in alternating order so
   drift on the machine does not decide the result.

## What the traces showed

Fast requests (about 10 ms): the API handler 9.6 to 10.5 ms, of which the RabbitMQ `publish` span is 4.6 to
5.1 ms, the consumer's `receive` span about 0.3 ms. Slowest requests (2.68 to 2.92 s): the `publish` span is
2.61 to 2.63 s in every one. So the publish step is both the largest unloaded cost (about 46% of the request) and
the whole of the tail. PostgreSQL's share is not directly visible (EF Core is not traced); the remainder of the
handler is small in every sample.

## Cause and fix

The publisher opened a channel, declared the exchange and closed the channel on every publish (three synchronous
round trips in the request). It now reuses one channel. The change, numbers, alternatives and consequences are in
`adr/0040-rabbitmq-publish-channel-reuse.md`.

## Variation

The original code measured 136, 93 and 127 rps at 10 users and 145, 120 and 157 rps at 25 users across three runs
(the first in the full baseline run, two write-only). That is a spread of about 25 to 30%. The fixed code measured
164 and 217 rps at 10 users and 337 and 292 rps at 25 users. The improvement at 25 users (about 2x) and the removal
of the 1.6 to 2.8 s tail are well outside that spread; small differences are not.

## Not established

- **Why** the old pattern stalled for a uniform ~2.6 s. The trace identifies the span, not the mechanism.
- How much of the remaining write latency is PostgreSQL versus the handler; EF Core is not instrumented.
- Behaviour above 25 users with the fix was not re-measured (only 10 and 25 were).
- Everything is on one laptop with all containers on the same host.

## A defect found in the measurement script

A run failed its first login with HTTP 500: `docker compose run k6` had re-created PostgreSQL and RabbitMQ under the
running Api (Compose logged "Recreated"), killing its pooled connections. It could not be reproduced in isolation,
so the cause is unknown. The script now runs k6 with `--no-deps` and waits for a working login before measuring.
