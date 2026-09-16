using Xunit;

namespace CustomsClearance.Platform.RulesEngine.Tests;

public class RuleVersionEffectiveDateValidatorTests
{
    private static RuleVersion Version(DateOnly effectiveFrom, DateOnly? effectiveTo) => new()
    {
        Id = Guid.NewGuid(),
        RuleId = Guid.NewGuid(),
        VersionNumber = 1,
        EffectiveFrom = effectiveFrom,
        EffectiveTo = effectiveTo,
        OutcomeFactName = "ClearanceType",
        OutcomeValue = "DEM",
    };

    [Fact]
    public void Validate_NoExistingVersions_NoOverlapNoSupersede()
    {
        var result = RuleVersionEffectiveDateValidator.Validate([], new DateOnly(2026, 1, 1), null);

        Assert.False(result.HasOverlap);
        Assert.Null(result.VersionToSupersede);
    }

    [Fact]
    public void Validate_NewVersionAfterOpenEndedExisting_SupersedesInsteadOfOverlapping()
    {
        var existing = Version(new DateOnly(2026, 1, 1), null);

        var result = RuleVersionEffectiveDateValidator.Validate([existing], new DateOnly(2027, 1, 1), null);

        Assert.False(result.HasOverlap);
        Assert.Same(existing, result.VersionToSupersede);
    }

    [Fact]
    public void Validate_NewVersionBeforeOpenEndedExisting_IsAnOverlapNotASupersede()
    {
        // New version starts BEFORE the existing open-ended one - not the
        // "extend into the future" supersede case, a real conflict.
        var existing = Version(new DateOnly(2026, 6, 1), null);

        var result = RuleVersionEffectiveDateValidator.Validate([existing], new DateOnly(2026, 1, 1), null);

        Assert.True(result.HasOverlap);
        Assert.Null(result.VersionToSupersede);
    }

    [Fact]
    public void Validate_NewVersionOverlapsClosedExistingVersion_IsAnOverlap()
    {
        var existing = Version(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var result = RuleVersionEffectiveDateValidator.Validate([existing], new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 1));

        Assert.True(result.HasOverlap);
    }

    [Fact]
    public void Validate_NewVersionAfterClosedExistingVersion_NoOverlap()
    {
        var existing = Version(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var result = RuleVersionEffectiveDateValidator.Validate([existing], new DateOnly(2027, 1, 1), null);

        Assert.False(result.HasOverlap);
        Assert.Null(result.VersionToSupersede);
    }

    [Fact]
    public void Validate_NewVersionExactlyAdjacentToClosedExistingVersion_NoOverlap()
    {
        // Existing ends 2026-12-31; new starts 2027-01-01 - adjacent, not overlapping.
        var existing = Version(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var result = RuleVersionEffectiveDateValidator.Validate([existing], new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31));

        Assert.False(result.HasOverlap);
    }

    [Fact]
    public void Validate_NewVersionSameDayAsClosedExistingVersionEnds_IsAnOverlap()
    {
        // Inclusive boundary - sharing the boundary day counts as overlap.
        var existing = Version(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var result = RuleVersionEffectiveDateValidator.Validate([existing], new DateOnly(2026, 12, 31), null);

        Assert.True(result.HasOverlap);
    }

    [Fact]
    public void Validate_MultipleClosedVersions_OnlyFlagsTheOneThatOverlaps()
    {
        var earlier = Version(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var later = Version(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var result = RuleVersionEffectiveDateValidator.Validate([earlier, later], new DateOnly(2027, 1, 1), null);

        Assert.False(result.HasOverlap);
    }
}
