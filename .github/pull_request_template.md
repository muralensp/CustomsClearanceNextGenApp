## Summary
<!-- What changed? Why? -->

## Linked Requirement
- PRD Chapter:
- Day-Wise Plan Day:
- ADR (if applicable):
- Work Item (docs/WORK-ITEMS.md):

## Impacted Modules
<!-- e.g. Declaration, Duty, Workflow -->

## API / Contract Changes
- [ ] OpenAPI updated
- [ ] AsyncAPI updated
- [ ] Breaking changes (major version bump required)

## Database Changes
- [ ] EF Core migration included
- [ ] Rollback considered/tested

## Testing
- [ ] Unit tests
- [ ] Integration tests
- [ ] Contract tests
- [ ] Manual verification
- [ ] Screenshots (UI, if applicable)

## Risks
<!-- Backward compatibility, performance, security -->

## Reviewer Checklist

Full checklists (formalized Day 15 — see `docs/quality-gates/`): [Gate 1 Architecture](../docs/quality-gates/Gate1_Architecture_Review.md) · [Gate 2 Backend](../docs/quality-gates/Gate2_Backend_Review.md) · [Gate 3 API](../docs/quality-gates/Gate3_API_Review.md) · [Gate 4 Frontend](../docs/quality-gates/Gate4_Frontend_Review.md) · [Gate 5 Release](../docs/quality-gates/Gate5_Release_Review.md). Quick summary for this PR:

- [ ] Architecture respected (bounded-context boundaries not violated — see ADR-0003, ADR-0006)
- [ ] No circular dependencies between context modules
- [ ] Logging added where appropriate
- [ ] Audit events added where appropriate (per PRD audit requirements)
- [ ] Tests passing
- [ ] `CI passed` and `Analyze (csharp)` checks are green (dependency scan, secret scan, coverage, lint included) - not merged over a red check
- [ ] Decision Trace verified (if a Rules Engine change, per ADR-0002)
