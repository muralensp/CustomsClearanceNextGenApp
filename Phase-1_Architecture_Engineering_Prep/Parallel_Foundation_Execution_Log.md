# Parallel-work Foundation Execution Log

Not a numbered plan day. Step 1 ("Foundation") of the multi-agent adoption plan agreed after reviewing
`Multi_Agent_Approach.docx`. Decision record: `adr/0035-api-host-modules-and-parallel-work-isolation.md`.

## What was done
1. Read `src/Api/Program.cs` (863 lines): seven copies of the schema-bootstrap retry block, per-capability
   registration, route guards and dev diagnostic endpoints, all in one file.
2. Extracted seven `IApiModule`s under `src/Api/Modules/` (code moved, not rewritten), a reflection
   `ApiModuleCatalog`, and one `DatabaseBootstrap`. `Program.cs` is now ~125 lines.
3. Parameterised `docker-compose.yml` host ports and image tags (defaults unchanged); added
   `build/scripts/agent-env.sh <slot>`; made `smoke-test.sh` / `e2e-smoke-test.sh` honour
   `GATEWAY_PORT` / `MAILPIT_UI_PORT`.
4. Wrote `docs/PARALLEL-WORK.md` (how to add a module, who owns each still-shared file, running a stack
   per worker, what a worker must hand back as evidence).
5. Added `ApiModuleCatalogTests` (8 tests).

## Problems found along the way
- `dotnet format` flagged my new private constants/fields (naming rule requires `_`); fixed. My rename
  also altered a log-message placeholder (`{MaxAttempts}`), caught on review and restored.
- The first port-collision check was vacuous (Python's `bash` was not Git Bash, so it saw no output and
  reported "none"). Re-run in bash it found 3 real collisions: SMTP stepped by 1000 landed on the Mailpit UI
  ports. SMTP now steps by 1; 110/110 ports distinct.
- A stray file was created one directory above the repo by a mistaken path; removed immediately, nothing
  else touched.

## Verification (unpiped exit codes)
Build 0 errors; format exit 0; full `dotnet test` exit 0 (integration 14/14); E2E 20/20 exit 0 on slot 0
and slot 1 started together; unmodified `smoke-test.sh` exit 0; `docker compose config` valid.

## Not done / limits
- Still-shared files are listed, not eliminated (sln, Api.csproj, Dockerfile COPYs, compose env list,
  appsettings, TestApiFactory blanking list, CLAUDE.md, ADR numbers).
- `contract-test.sh` still targets localhost:8080.
- Concurrency was tested with two stacks, not sustained load; CPU/memory is the real ceiling.
- Skills extraction, the Days 41-44 independence check and the pilot (plan steps 2-3) are not started.
- Nothing committed or pushed.
