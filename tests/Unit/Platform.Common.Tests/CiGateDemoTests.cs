using Xunit;

namespace CustomsClearance.Platform.Common.Tests;

public class CiGateDemoTests
{
    // Deliberately broken (docs/BRANCH-PROTECTION.md section 3). Never merge.
    [Fact]
    public void Demo_BrokenOnPurpose() => Assert.True(false);
}
