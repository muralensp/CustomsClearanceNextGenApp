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

## 3. Security items needing no new design (ADR-0041)

Product Owner took the recommendations for all three.

- **`/metrics` (checklist 7.5)**: served only on an internal Gateway port (9100, not published); a 404 on the public port.
  The check uses the connection's local port, not the Host header (spoofable). Prometheus scrapes `gateway:9100`.
- **Token lifetime (1.6)**: `Jwt:AccessTokenMinutes`, default 60 (was a hard-coded 8 h), range 1 to 1440, validated at
  start-up. No refresh flow yet.
- **Login throttling (1.7)**: documented as an accepted gap, deliberately not built (needs design decisions best made
  with the identity provider).

**Live verification**: `/metrics`, `/Metrics`, `/metrics/`, `/metrics?x=1`, POST and a spoofed `Host: localhost:9100` all
404 on 8080; port 9100 unreachable from the host; Prometheus targets for gateway (`gateway:9100`) and api both up;
`/health` and `/__gateway/health` 200; login expiry 60.0 minutes. Security unit tests 28 (from 18).

**Not verified / limits**: the before-state of `/metrics` was not re-run live (it was found by reading the code on Day 41);
the new Gateway middleware has no unit test (the Gateway has no test project); refresh tokens and lockout remain
unbuilt.

## 4. India tariff-sourcing research (ADR-0042)

Web research only, no code. Output: `docs/research/INDIA-TARIFF-SOURCING-OPTIONS.md` (sources, update cadence, four
Phase 1 options, open questions for the Product Owner).

- **Found**: the Customs Tariff (CBIC), ITC-HS (DGFT) and the effective duty stack (ICEGATE) are three separate data sets;
  official publication appears to be PDFs and web tools, with no bulk download or API found; nomenclature changes with
  the Finance Act, rates by notification at any time; DGFT Notification 26/2026-27 (27 Jul 2026) aligned Schedule II with
  the Finance Act 2026; the next HS edition is reported for 1 Jan 2028.
- **Not established**: whether any official machine-readable feed or reuse notice exists (cbic.gov.in failed with a
  certificate error in the fetch tool); whether data.gov.in has a tariff dataset; vendor prices and terms; India's HS 2028
  adoption date. Most facts rest on secondary sources and are marked *(unverified)* in the note.
- **Recommendation, not a decision**: evaluate a hybrid; get vendor quotes; never scrape ICEGATE without written
  confirmation. Needs the Product Owner's answers to four questions in the note.
