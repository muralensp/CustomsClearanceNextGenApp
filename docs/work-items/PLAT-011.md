---
key: PLAT-011
type: feature
priority: 1
depends_on: []
adr: none
requires_approval: false
---
# PLAT-011: Map a NotFoundException to a 404 problem response

**Description:** Pilot task (low risk). `ErrorMapper` has no case for a missing resource, so any such error would surface as a 500. Add a `NotFoundException` and map it to a 404 problem response. Nothing throws it yet, so no endpoint changes behaviour.

**Acceptance criteria:**
- `ErrorMapper.Map` returns status 404, type `https://api.customsclearance.india/errors/not-found` and title `The requested resource was not found` for a `NotFoundException`
- The response carries the instance path and the correlation id, and `Errors` is null
- Unit tests in `tests/Unit/Platform.ErrorHandling.Tests/` cover the new case, and the existing tests still pass
- Only files under `src/Platform/ErrorHandling/` and `tests/Unit/Platform.ErrorHandling.Tests/` change
