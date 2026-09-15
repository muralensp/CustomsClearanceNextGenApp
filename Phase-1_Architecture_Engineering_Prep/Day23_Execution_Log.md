# Day 23 Execution Log — Identity & Access

Phase 0, Week 5 (Core Infrastructure), Day 23 of the Day-Wise Plan. Deliverable: "Working login +
role-based token issuance."

---

## Step 1 — Research what already exists before building

Dispatched an exploration pass over `src/Platform/Security/`, `src/Api/Program.cs`,
`tests/Unit/Platform.Security.Tests/`, `adr/0011`, and the PRD's persona section before writing
any code. Found: full role/policy model and JWT **Bearer validation** already exist (Day 17), but
**zero token-issuance code** anywhere - `JwtOptions`' own doc comment already named this as "the
local/Development mode... before the real Identity service exists (Phase 0, Day 23)," confirming
this day was an anticipated gap, not a surprise.

## Step 2 — Confirm two decisions before building

Asked the user directly rather than assuming:
1. **In-house minimal Identity** (not a managed provider) - consistent with `JwtOptions`' own
   phasing language and Day 21's deferred cloud commitment.
2. **Real Postgres-backed user store** (not in-memory) - consistent with this project's standing
   "prove against the real running system" discipline and the Day 13/18 Diagnostics precedent.

Recorded both, plus the "Identity lives inside the Api host, not a new deployable" call (unlike
Day 22's Gateway, nothing requires Identity to be network-separate), as `adr/0017-identity-and-
access.md`.

## Step 3 — Build

- `src/Platform/Security/ITokenIssuer.cs` / `JwtTokenIssuer.cs` - issues JWTs signed with the same
  symmetric key Bearer validation checks against. Registered via a new
  `AddCustomsClearanceTokenIssuance(IConfiguration)`, kept separate from
  `AddCustomsClearanceAuthentication` (issuance vs. validation are distinct responsibilities).
- `src/Api/Identity/IdentityDbContext.cs` - `PlatformUser` (`AuditableEntity<Guid>`), same
  "not a real bounded context" placeholder status as `Diagnostics/DiagnosticsDbContext`.
- `src/Api/Identity/IdentitySeeder.cs` - seeds one dev user per PRD persona (Ch.1.7):
  `broker`/`supervisor`/`compliance`/`admin`, passwords hashed via
  `Microsoft.AspNetCore.Identity.PasswordHasher<TUser>` (PBKDF2), never plaintext.
- `src/Api/Identity/LoginModels.cs` - `LoginRequest`/`LoginResponse`.
- `POST /auth/login` mapped in `Program.cs`, gated behind the same connection-string-presence
  guard as the Diagnostics widgets endpoints (`RequestDelegateFactory` validates parameter
  bindability at map time - an unregistered `IdentityDbContext` parameter would otherwise crash
  the entire app at startup, a standing lesson from Day 18).
- `ConnectionStrings:IdentityDb` added to `appsettings.Development.json` and `docker-compose.yml`
  (same `Host=postgres` vs `Host=localhost` override pattern as `DiagnosticsDb`).

## Step 4 — Three real bugs found and fixed during verification (not assumed away)

1. **Integration tests crashed the whole host at startup.** `TestApiFactory` blanked
   `ConnectionStrings__DiagnosticsDb` (Day 18's fix) but not the new `ConnectionStrings__IdentityDb`
   - the exact same standing lesson recurring for a second connection string. Fixed by extending
   the same static-constructor override.
2. **`relation "Users" does not exist` crash on first login.** `IdentityDbContext.Database.
   EnsureCreatedAsync()` only creates the physical *database* if missing - it silently no-ops
   (creates no tables) when the database already exists, which it did (Diagnostics, registered
   first, already created it). A second `DbContext` sharing that physical database never got its
   own tables. Fixed with a fallback: check `EnsureCreatedAsync`'s return value, and when `false`,
   call `identityDb.GetService<IDatabaseCreator>().CreateTablesAsync()` (this context's tables
   only), tolerating `PostgresErrorCodes.DuplicateTable` for idempotent restarts. Verified against
   both a warm restart *and* a full `docker compose down -v` (fresh volume) cycle.
3. **Coverage gate failure** (ADR-0007's 80% threshold on `Platform.Security`) - new issuance code
   had no direct unit tests. Added `JwtTokenIssuerTests` (claim correctness, expiry, and a
   round-trip proof that an issued token is accepted by this library's own Bearer validation) and
   `ServiceCollectionExtensionsTests` (missing-key failure path, successful DI resolution).
   Extracted `AuthenticationAuthorizationTests`' private test-host builder into a shared
   `TestAuthHost` helper rather than duplicating it. Also fixed two `dotnet format` IDE1006
   (missing `_` prefix) violations in the new code.

**Meta-finding**: the first two verification passes (`dotnet format`/`dotnet test`) were piped
through `tail`, which silently reported `tail`'s exit code (0) instead of the real command's -
exactly the standing lesson CLAUDE.md already documents about piped exit codes. Both were
non-zero underneath (2 format violations, then a coverage-gate failure) and only surfaced once
re-run unpiped. No claim in this log is based on a piped result.

## Step 5 — Prove the deliverable end-to-end against real running containers

1. `docker compose down -v` + `up -d --build` (fresh volume, proving the table-creation fix from
   a genuinely clean state, not a warm restart that could mask the original bug).
2. `POST /auth/login {"username":"admin","password":"Admin-Dev-Pw-123!"}` (through the Day 22
   gateway) → 200, real JWT returned.
3. That token against `/diagnostics/secure` (`RequireAdmin` policy) → 200, authenticated as Admin.
4. Login as `broker`, use that token against the same Admin-only endpoint → **403** (role-based
   rejection proven, not just role-based acceptance).
5. Wrong password → 401. Unknown username → 401. No token at all → 401.
6. Full-solution `dotnet build`/`dotnet format --verify-no-changes`/`dotnet test` all clean with
   **unpiped** exit codes (0/0/0).
7. `build/scripts/smoke-test.sh` (the actual CI script) re-run, unpiped exit code 0.

## Outcome

**Deliverable met**: working login (`POST /auth/login`) issuing real, role-based JWTs, proven
against real Postgres and real role-policy enforcement (200 for the right role, 403 for the wrong
one, 401 for no/bad credentials). **Three real bugs found and fixed**, two of them recurrences of
already-documented standing lessons (piped exit codes; `WebApplicationFactory` connection-string
overrides) - worth flagging that "documented once" didn't prevent recurrence, only faster
diagnosis once it happened. **Decision recorded**: `adr/0017-identity-and-access.md` (Accepted).

**Status**: Complete. Next unstarted item is **Day 24 — Event Bus**, pending explicit instruction
to begin.
