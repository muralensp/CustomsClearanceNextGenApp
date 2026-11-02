# PLAT-011 progress

- Added `src/Platform/ErrorHandling/NotFoundException.cs` (sealed, message ctor).
- `ErrorMapper.Map` now returns 404 / not-found type / "The requested resource was not found" title, Detail = message, Errors null.
- ErrorHandling unit tests: 12 passed (incl. the new NotFoundException test).
- Not run: `dotnet format --verify-no-changes` (blocked by permission prompt); the change follows existing style.
- Remaining: nothing known.
