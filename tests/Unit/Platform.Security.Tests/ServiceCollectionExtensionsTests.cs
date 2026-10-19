using CustomsClearance.Platform.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CustomsClearance.Platform.Security.Tests;

/// <summary>
/// Covers AddCustomsClearanceTokenIssuance (Day 23) directly: both the
/// missing-key failure path (mirrors AddCustomsClearanceAuthentication's
/// own validation, per that method's doc comment) and successful
/// resolution of a working ITokenIssuer from DI.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfig(string? key) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = key,
                ["Jwt:Issuer"] = "https://test.customsclearance.india",
                ["Jwt:Audience"] = "customsclearance-api-tests",
            })
            .Build();

    [Fact]
    public void AddCustomsClearanceTokenIssuance_MissingKey_Throws()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(key: null);

        Assert.Throws<InvalidOperationException>(() => services.AddCustomsClearanceTokenIssuance(config));
    }

    [Fact]
    public void AddCustomsClearanceTokenIssuance_ValidKey_RegistersWorkingTokenIssuer()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(key: "this-is-a-test-only-signing-key-not-for-production-use-32bytes");

        services.AddCustomsClearanceTokenIssuance(config);
        using var provider = services.BuildServiceProvider();

        var tokenIssuer = provider.GetRequiredService<ITokenIssuer>();
        var issued = tokenIssuer.IssueToken(Guid.NewGuid(), "admin", PlatformRoles.Admin);

        Assert.False(string.IsNullOrWhiteSpace(issued.AccessToken));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1441")]
    public void AddCustomsClearanceTokenIssuance_OutOfRangeLifetime_ThrowsAtRegistration(string minutes)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "this-is-a-test-only-signing-key-not-for-production-use-32bytes",
                ["Jwt:Issuer"] = "https://test.customsclearance.india",
                ["Jwt:Audience"] = "customsclearance-api-tests",
                ["Jwt:AccessTokenMinutes"] = minutes,
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddCustomsClearanceTokenIssuance(config));

        Assert.Contains("AccessTokenMinutes", ex.Message);
    }
}
