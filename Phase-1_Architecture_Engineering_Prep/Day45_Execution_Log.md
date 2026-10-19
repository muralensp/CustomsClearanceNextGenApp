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

## 2. Low-risk Dependabot batch

**Merged (8 PRs, squash, no conflicts)**: dotnet-reportgenerator 5.5.10 to 5.5.11 (#19), coverlet.collector 6.0.0 to
6.0.4 (#17, one project), coverlet.msbuild 6.0.0 to 6.0.4 (#18, all 12 declaring test projects, so versions stay
consistent), and the GitHub Actions bumps cache 4 to 6 (#13), upload-artifact 4 to 7 (#10), setup-dotnet 4 to 6 (#9),
checkout 4 to 7 (#11), codeql-action 3 to 4 (#8). Each PR's own CI had passed with its bumped version; `CI passed` was
re-checked before each merge. The combined result was verified on `main` (`a8306d5`): CI 11 of 11 jobs and CodeQL
success.

**Closed, not merged**: the four .NET 8 to 10 image PRs (#1, #2, #4, #5). The SDK and runtime images must change
together, and #2/#5 (runtime only) failed CI; moving to .NET 10 is a separate task. `.github/dependabot.yml` now
ignores major updates of `dotnet/sdk` and `dotnet/aspnet` in both Docker sections (the NuGet section already pinned
majors per ADR-0001), so they are not reopened.

**Left open on purpose** (need a deliberate check that CI does not provide, e.g. that dashboards and alert rules still
load): Grafana 11 to 13 (#7), Tempo 2.5 to 3.1 (#14), Prometheus 2 to 3 (#3), Promtail 2.9 to 3.6 (#12), Mailpit
(#6), the Microsoft/EF Core group of 13 (#15) and the OpenTelemetry group (#16).
