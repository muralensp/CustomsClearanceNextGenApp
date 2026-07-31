using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CustomsClearance.Api.IntegrationTests;

/// <summary>
/// Placeholder integration test (Day 14) proving the integration test
/// framework works end-to-end: spins up the real Api host in-process via
/// WebApplicationFactory (no docker required for this layer - that is what
/// the separate contract-test layer, against a running container, is for)
/// and exercises the actual HTTP pipeline, not just the Program.cs code
/// directly. Replace/extend with real endpoint tests as each bounded
/// context's API is implemented.
/// </summary>
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHealth_ReturnsOkWithHealthyStatus()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", body);
        Assert.Contains("CustomsClearance.Api", body);
    }
}
