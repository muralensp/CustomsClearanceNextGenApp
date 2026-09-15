# Bounded-Context Project Template

Added Day 21 (ADR-0015), as the modular-monolith reinterpretation of the Day-Wise Plan's "base
service template project." This is **not** a new containerized service — this project is a
Modular Monolith (ADR-0003), so every bounded context ships inside the single `src/Api` host. This
template exists to make starting a new context's real build (Phase 3+) consistent with the project
split ADR-0006 already committed to, instead of re-deriving it from scratch each time.

## How to use, when a context's real build starts

1. Copy both `.csproj.template` files into the target `src/{Context}/` folder as:
   - `src/{Context}/{Context}.csproj` (from `Context.csproj.template`)
   - `src/{Context}/{Context}.Infrastructure.csproj` (from `Context.Infrastructure.csproj.template`)
2. Rename every `Context` placeholder in both files' contents and the `ProjectReference` paths to
   the real context name (e.g. `Declaration`, `Compliance`).
3. Add both projects to `CustomsClearance.sln`.
4. Add a `ProjectReference` from `src/Api/CustomsClearance.Api.csproj` to the new
   `{Context}.Infrastructure.csproj` (Infrastructure references Context, never the reverse —
   ADR-0006) — and only if that context is being wired into the running host yet; some contexts
   may exist as buildable projects before they're wired up.
5. Run Gate 1 (`docs/quality-gates/Gate1_Architecture_Review.md`) against the new module before
   merging, per its own instruction to run "at the start of implementing any new bounded context
   module."

## What's deliberately not templated here

- A `Context.csproj`/`Context.Infrastructure.csproj` pair covers Domain+Application+Infrastructure
  per ADR-0006. Test projects (`tests/Unit/{Context}.Tests/`) follow the existing pattern in
  `tests/Unit/Platform.*.Tests/` instead — not duplicated here to avoid drifting out of sync with
  whatever the test-project template looks like by the time it's needed.
- No `Dockerfile` — Modular Monolith means one container (`src/Api/Dockerfile`), not one per
  context. See ADR-0015 for why "hello world in a container" didn't need a new scaffold.

## Related

ADR-0003 (Modular Monolith), ADR-0006 (Clean Architecture adaptation), ADR-0012
(`IDeclarationRepository`-over-`IRepository` narrower-port pattern), ADR-0015 (this template's
Day 21 origin).
