# ADR-0017: Identity & Access (Day 23)

**Status:** Accepted (dev/CI only; superseded for production by ADR-0044)
**Date:** 2026-09-15
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Day 23 of the Day-Wise Plan ("Identity & Access") calls for: implement an Identity
service (or integrate a managed identity provider); user/role model per PRD personas (Broker,
Supervisor, Compliance Officer, Admin). Deliverable: "Working login + role-based token issuance."

`Platform.Security` (ADR-0011, Day 17) already had the role/policy model (`PlatformRoles`,
`PlatformPolicies`) and JWT **Bearer validation** — but zero **issuance** code (no `/login`, no
token-generation service). `JwtOptions`' own doc comment already named symmetric-key validation
as "the local/Development mode... before the real Identity service exists (Phase 0, Day 23)," so
this ADR was an anticipated, not improvised, next step.

Two decisions were confirmed with the user before building:

1. **In-house minimal Identity vs. a managed provider** (Azure AD B2C, Auth0, etc.). Confirmed
   **in-house**: consistent with `JwtOptions`' own framing (symmetric-key issuance now, a
   config-only swap to Authority/OIDC validation later), and with Day 21's deferral of any
   cloud/hosting commitment (ADR-0015) — a managed provider would require exactly the external
   account this project has twice declined to commit to yet.
2. **User store backing**: a real Postgres-backed table vs. an in-memory dev-only seed. Confirmed
   **Postgres-backed**, consistent with this project's standing discipline ("nothing is done until
   proven against the real running system") and the Day 13/18 Diagnostics-table precedent.

## Decision

1. **`Platform.Security` gains issuance, not just validation**: `ITokenIssuer`/`JwtTokenIssuer`
   (`src/Platform/Security/ITokenIssuer.cs`, `JwtTokenIssuer.cs`) mint a JWT signed with the same
   symmetric key `AddCustomsClearanceAuthentication` validates against — a token issued here is
   immediately accepted by this same host's Bearer validation. Registered via a new
   `AddCustomsClearanceTokenIssuance(IConfiguration)` method, kept separate from
   `AddCustomsClearanceAuthentication` because they're distinct responsibilities (a host that only
   *validates* Authority-issued tokens, per the future migration `JwtOptions` already anticipates,
   would not call this).
2. **Identity capability lives inside the `Api` host, not a new deployable.** Unlike the Gateway
   (ADR-0016), nothing in Day6_API_Standards or ADR-0004 requires login to be a separate service —
   it's a capability of the platform, not an ingress-boundary concern. Consistent with ADR-0015's
   prediction that Days 22-25 would mostly become "middleware/library concerns wired into the
   single `Api` host" rather than standalone services (Day 22's Gateway was the justified
   exception, driven by a real external-ingress standard; Day 23 isn't).
3. **`src/Api/Identity/`** (same "not a real bounded context" placeholder status as
   `src/Api/Diagnostics/`, Day 13/18 — a real user/identity bounded context is
   Administration/PartyManagement's later, real scope):
   - `IdentityDbContext` / `PlatformUser` (`AuditableEntity<Guid>`: Username, PasswordHash, Role).
   - `IdentitySeeder`: seeds exactly one dev user per PRD persona (`broker`, `supervisor`,
     `compliance`, `admin`) with clearly-marked dev-only passwords, hashed via
     `Microsoft.AspNetCore.Identity.PasswordHasher<TUser>` (PBKDF2) — never plaintext, never a
     registration flow (out of scope; a real Administration context owns user management later).
   - `POST /auth/login` (`LoginRequest`/`LoginResponse`): looks up the user, verifies the password
     hash, and on success calls `ITokenIssuer.IssueToken(...)` — a real capability, not a
     diagnostic, but gated behind the same connection-string-presence guard as the Diagnostics
     widgets endpoints (`RequestDelegateFactory` validates parameter bindability at *map* time, so
     an unregistered `IdentityDbContext` would otherwise crash the entire app at startup, per the
     Day 18 finding this project already carries as a standing lesson).
4. **`ConnectionStrings:IdentityDb`**: a separate config key from `DiagnosticsDb`, defaulting to
   the same physical dev database (so this can later point at a dedicated store without touching
   Diagnostics) — same override pattern as Day 18/22 (`Host=postgres` in `docker-compose.yml` vs.
   `Host=localhost` in `appsettings.Development.json`).

### Real bugs found and fixed during verification

1. **Integration tests crashed the whole host at startup** (`ConnectionStrings__IdentityDb` not
   blanked in `TestApiFactory`) — the exact standing lesson CLAUDE.md already documents about
   `WebApplicationFactory` and top-level-statement config timing (Day 18's finding), now recurring
   for a second connection string. Fixed by extending `TestApiFactory`'s static constructor to
   also blank `ConnectionStrings__IdentityDb`, mirroring `DiagnosticsDb`'s existing override.
2. **`IdentityDbContext.Database.EnsureCreatedAsync()` silently created no tables**, crashing the
   `Api` on first login with `relation "Users" does not exist`. Root cause: `EnsureCreatedAsync`
   only creates the **database** if missing — it no-ops when the database already exists, which it
   did here (Diagnostics, registered first, already created it). A second `DbContext` sharing the
   same physical database therefore never got its own tables created. Fixed by checking
   `EnsureCreatedAsync`'s return value and falling back to
   `identityDb.GetService<IDatabaseCreator>()` → `CreateTablesAsync()` (this context's tables
   only) when the database already existed, tolerating `PostgresErrorCodes.DuplicateTable` for
   idempotent container restarts against a persisted volume. Confirmed via a full `docker compose
   down -v` (fresh volume) → `up --build` cycle, not just a warm restart.
3. **Coverage gate failure** (ADR-0007's 80% threshold on `Platform.Security`, a CRUD/library-tier
   project) — new `JwtTokenIssuer`/`AddCustomsClearanceTokenIssuance` code had no direct unit
   tests. Fixed by adding `JwtTokenIssuerTests` (claim correctness, expiry, and a round-trip proof
   that an issued token is actually accepted by this library's own Bearer validation) and
   `ServiceCollectionExtensionsTests` (missing-key failure path, successful DI resolution).
   Extracted the existing `AuthenticationAuthorizationTests`' private test-host builder into a
   shared internal `TestAuthHost` helper to avoid duplicating it, rather than copy-pasting.
   `dotnet format`'s IDE1006 naming-convention check (private fields need a `_` prefix) also
   caught two violations in the new code (`JwtTokenIssuer._tokenLifetime`,
   `IdentitySeeder._devUsers`), fixed the same way.

All three were caught by actually running the full test suite and a real `docker compose` cycle
with **unpiped** exit codes — the first two runs of `dotnet format`/`dotnet test` in this session
were piped through `tail`, which silently reported the pipeline's last command's exit code (0)
instead of the real failure, exactly the standing lesson CLAUDE.md already warns about. Re-running
unpiped surfaced the true failures.

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Managed identity provider (Azure AD B2C, Auth0) | Production-grade from day one | Requires an external account/tenant commitment this project has deliberately deferred twice (ADR-0008, ADR-0015) | User declined |
| In-memory seeded users (no real DB) | Faster to build | Weaker proof than this project's own standing discipline; Administration/PartyManagement (the real future home) stay empty either way, so no meaningful future rework is saved | User declined |
| Identity as a separate deployable (mirroring the Gateway) | Symmetry with Day 22 | No standard (unlike Day6 API Standards Section 2 for the gateway) requires this; adds a container/service for a capability with no ingress-boundary reason to be separate | Rejected - Day 22's separateness was driven by an already-documented standard, not a general "infra pieces are services" rule |

## Consequences

- `templates/BoundedContextTemplate/` (ADR-0015) remains unused - Identity's real home
  (Administration or PartyManagement) is still empty; `src/Api/Identity/` is explicitly a
  placeholder, same status as Diagnostics, to be replaced once that bounded context is built for
  real (Phase 3+).
- The `EnsureCreatedAsync`-then-`CreateTablesAsync`-fallback pattern found here will recur for any
  future `DbContext` added to this same physical dev database before real EF Core migrations
  exist (Phase 0/3) - worth checking for on the next one, not re-discovering from scratch.
- (Update Day 45, ADR-0041: the lifetime is now the `Jwt:AccessTokenMinutes` setting, default 60.) 8-hour token expiry with no refresh-token flow is a Day 23 placeholder; re-login is the only
  renewal path for now - acceptable for a dev-time Identity stand-in, revisit once real usage
  patterns exist.
- No registration/password-reset/user-management endpoints exist - intentionally out of scope;
  that's Administration's real, later work.

## Related

[[ADR-0011]] (Security & Audit libraries - the Bearer validation this ADR adds issuance
alongside), [[ADR-0012]] (Persistence - `PlatformDbContext`/`EfRepository` this reuses),
[[ADR-0015]] (Day 21 - predicted Days 22-25's Modular-Monolith reinterpretation), [[ADR-0016]]
(Day 22 Gateway - the contrasting case where a separate deployable *was* justified), PRD Ch.1.7
(personas this seeds one dev user per).
