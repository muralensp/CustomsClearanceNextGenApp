---
key: PLAT-013
type: feature
priority: 1
depends_on: []
adr: none
requires_approval: false
---
# PLAT-013: Map a ConflictException to a 409 problem response

**Description:** Pilot task (low risk), the second ordinary task, to check the PR title, body and Verification section after ADLC-039. `ErrorMapper` has no case for a conflict with the current state of a resource (a duplicate, a stale update), so any such error would surface as a 500. Add a `ConflictException` and map it to a 409 problem response, the same way `NotFoundException` is mapped to a 404. Nothing throws it yet, so no endpoint changes behaviour.

**Acceptance criteria:**
- `ErrorMapper.Map` returns status 409, type `https://api.customsclearance.india/errors/conflict` and title `The request conflicts with the current state of the resource` for a `ConflictException`
- The response carries the instance path and the correlation id, and `Errors` is null
- The response's `Detail` carries the exception message, and a test asserts it
- Unit tests in `tests/Unit/Platform.ErrorHandling.Tests/` cover the new case, and the existing tests still pass
- Only files under `src/Platform/ErrorHandling/` and `tests/Unit/Platform.ErrorHandling.Tests/` change
