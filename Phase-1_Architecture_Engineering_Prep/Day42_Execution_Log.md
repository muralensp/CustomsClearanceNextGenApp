# Day 42 Execution Log - Performance Baseline

Branch `day-42/performance-baseline` (base `19d1b7c`). ADR: `adr/0037-performance-baseline.md`. Report:
`docs/performance/BASELINE-2026-10-19.md`.

## What was done

1. Asked the Product Owner four design questions (rate limiter, tool, scenarios, where results live); answers
   recorded in ADR-0037.
2. Gateway rate limit made configurable (`src/Gateway/RateLimitingOptions.cs`, `Program.cs`), defaults unchanged;
   compose passes it through.
3. k6 service under compose profile `perf`; `tests/Performance/baseline.js` (3 paths x 4 levels);
   `build/scripts/perf-baseline.sh` (environment capture, stack start with the RabbitMQ retry, warm-up, container
   CPU/memory sampling, k6 run, teardown).
4. Quick run, fix, real run, report.

## Problems found

- Git Bash on Windows rewrote the container path `/scripts/baseline.js` into `C:/Program Files/Git/scripts/...`;
  fixed with `MSYS_NO_PATHCONV=1` on that call.
- The first results table divided requests by the whole test duration (55 s) instead of each level's 5 s, so
  every rps figure was about 11x too low (health at 1 user showed 18, really about 200). Caught by comparing
  requests with rps by hand; fixed before the real run.

## Verification (exit codes captured directly)

- `dotnet build` of the Gateway: exit 0, 0 warnings.
- Default limiter unchanged: a 150-request burst gave 131 x 200 and 19 x 429 (the burst straddles a fixed-window
  boundary; Day 22 saw 121/29), and the container shows `RateLimiting__PermitLimit=100`, `WindowSeconds=10`.
  The raised value works: more than 3,500 requests in 5 s with zero 429s in the quick run.
- `docker compose config`: exit 0; `k6` appears only with `--profile perf`.
- Quick run (5 s, 1 and 10 users): exit 0. Full run (30 s, 1/10/25/50 users): exit 0, `k6_exit=0`, 12 scenarios,
  zero failed requests, zero 429s, no containers left afterwards.
- Raw outputs are in `TestResults/perf/` (git-ignored); the report carries the table and the environment.

## Not verified / limits

One run only, so variation is unknown; no repeat. The write-path cause was not investigated. The rules-engine
path, login and the Api without the Gateway were not measured. The new Gateway option has no unit test (the
Gateway has no test project); it is covered by the live check above.
