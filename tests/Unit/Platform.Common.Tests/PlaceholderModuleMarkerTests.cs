using CustomsClearance.Platform.Common;
using Xunit;

namespace CustomsClearance.Platform.Common.Tests;

/// <summary>
/// Placeholder test proving the CI pipeline (Day 11) can build, and run a real test
/// against, the scaffold. Replace with real tests once Phase 0 implementation begins.
/// </summary>
public class PlaceholderModuleMarkerTests
{
    [Fact]
    public void ModuleName_IsPlatformCommon()
    {
        Assert.Equal("Platform.Common", PlaceholderModuleMarker.ModuleName);
    }

    [Fact]
    public void Describe_ReturnsModuleNameWithPrefix()
    {
        Assert.Equal("Module: Platform.Common", PlaceholderModuleMarker.Describe());
    }
}
