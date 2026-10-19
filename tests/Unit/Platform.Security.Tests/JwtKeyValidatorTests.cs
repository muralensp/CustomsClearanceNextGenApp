using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CustomsClearance.Platform.Security.Tests;

/// <summary>
/// Day 41 (ADR-0036): a host outside Development must fail at startup rather
/// than validate tokens with the committed (public) placeholder key or a key
/// too short for HMAC-SHA256; Development must keep working unchanged.
/// </summary>
public class JwtKeyValidatorTests
{
    private const string _placeholderKey = "LOCAL-DEV-ONLY-PLACEHOLDER-KEY-NEVER-USE-IN-PRODUCTION-32B";
    private const string _strongKey = "a-real-looking-random-signing-key-0123456789-abcdef";

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public FakeEnvironment(string name)
        {
            EnvironmentName = name;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = ".";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

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
    public void Development_PlaceholderKey_IsAllowed()
    {
        var exception = Record.Exception(() =>
            JwtKeyValidator.EnsureAcceptable(new JwtOptions { Key = _placeholderKey }, new FakeEnvironment("Development")));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void NonDevelopment_PlaceholderKey_Throws(string environmentName)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            JwtKeyValidator.EnsureAcceptable(new JwtOptions { Key = _placeholderKey }, new FakeEnvironment(environmentName)));

        Assert.Contains("placeholder", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(environmentName, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonDevelopment_ShortKey_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            JwtKeyValidator.EnsureAcceptable(new JwtOptions { Key = "too-short" }, new FakeEnvironment("Production")));

        Assert.Contains("shorter", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonDevelopment_StrongKey_IsAllowed()
    {
        var exception = Record.Exception(() =>
            JwtKeyValidator.EnsureAcceptable(new JwtOptions { Key = _strongKey }, new FakeEnvironment("Production")));

        Assert.Null(exception);
    }

    [Fact]
    public void UnknownEnvironment_IsTreatedAsNotDevelopment()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            JwtKeyValidator.EnsureAcceptable(new JwtOptions { Key = _placeholderKey }, environment: null));

        Assert.Contains("(unknown)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAuthentication_ProductionWithPlaceholderKey_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() =>
            services.AddCustomsClearanceAuthentication(BuildConfig(_placeholderKey), new FakeEnvironment("Production")));
    }

    [Fact]
    public void AddAuthentication_DevelopmentWithPlaceholderKey_Succeeds()
    {
        var services = new ServiceCollection();

        var exception = Record.Exception(() =>
            services.AddCustomsClearanceAuthentication(BuildConfig(_placeholderKey), new FakeEnvironment("Development")));

        Assert.Null(exception);
    }

    [Fact]
    public void AddTokenIssuance_ProductionWithPlaceholderKey_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() =>
            services.AddCustomsClearanceTokenIssuance(BuildConfig(_placeholderKey), new FakeEnvironment("Production")));
    }

    [Fact]
    public void AddTokenIssuance_ProductionWithStrongKey_Succeeds()
    {
        var services = new ServiceCollection();

        var exception = Record.Exception(() =>
            services.AddCustomsClearanceTokenIssuance(BuildConfig(_strongKey), new FakeEnvironment("Production")));

        Assert.Null(exception);
    }
}
