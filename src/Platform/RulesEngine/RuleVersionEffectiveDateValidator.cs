namespace CustomsClearance.Platform.RulesEngine;

public sealed record EffectiveDateValidationResult(bool HasOverlap, RuleVersion? VersionToSupersede);

/// <summary>
/// Pure logic (Day 34) behind the admin API's "add a new RuleVersion"
/// invariant ADR-0013/ADR-0025 deferred to this day: no two RuleVersions
/// of the same Rule may have overlapping effective-date windows -
/// EXCEPT the normal authoring case of superseding an existing
/// open-ended version (EffectiveTo == null) with a later one, which is
/// not a conflict but the routine way a policy gets updated going
/// forward. Extracted from the endpoint itself (RuleAdminEndpoints, Api
/// project - no dedicated unit test project) so this - the one place a
/// real authoring bug could silently corrupt effective-dating - is
/// covered by the same 95% core-tier gate (ADR-0007) as the rest of
/// Platform.RulesEngine, not proven only by live docker-compose testing.
/// </summary>
public static class RuleVersionEffectiveDateValidator
{
    public static EffectiveDateValidationResult Validate(IReadOnlyList<RuleVersion> existingVersions, DateOnly newEffectiveFrom, DateOnly? newEffectiveTo)
    {
        var supersededVersion = existingVersions.SingleOrDefault(v => v.EffectiveTo is null && v.EffectiveFrom < newEffectiveFrom);

        var newEnd = newEffectiveTo ?? DateOnly.MaxValue;
        var hasOverlap = existingVersions
            .Where(existing => existing != supersededVersion)
            .Any(existing =>
            {
                var existingEnd = existing.EffectiveTo ?? DateOnly.MaxValue;
                return newEffectiveFrom <= existingEnd && existing.EffectiveFrom <= newEnd;
            });

        return new EffectiveDateValidationResult(hasOverlap, supersededVersion);
    }
}
