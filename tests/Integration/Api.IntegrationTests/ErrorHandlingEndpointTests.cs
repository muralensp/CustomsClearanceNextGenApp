using System.Net;
using System.Text.Json;
using Xunit;

namespace CustomsClearance.Api.IntegrationTests;

/// <summary>
/// Proves Platform.Logging + Platform.ErrorHandling (Day 16) work correctly
/// when wired into the real Api host end-to-end, via WebApplicationFactory
/// against the Development-only /diagnostics/throw-validation-error
/// endpoint - the same mechanism verified manually (curl) during Day 16,
/// now captured as a repeatable automated test.
/// </summary>
public class ErrorHandlingEndpointTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public ErrorHandlingEndpointTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ThrowValidationError_ReturnsUnprocessableEntityWithProblemDetails()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "integration-test-corr-id");

        var response = await client.GetAsync("/diagnostics/throw-validation-error");

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        response.Headers.TryGetValues("X-Correlation-Id", out var correlationHeaderValues);
        Assert.Equal("integration-test-corr-id", correlationHeaderValues?.FirstOrDefault());

        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.Equal("integration-test-corr-id", body.GetProperty("correlationId").GetString());
        Assert.Equal("BR-DEMO-001", body.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task ThrowValidationError_WithoutRequestCorrelationId_GeneratesOne()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/diagnostics/throw-validation-error");

        response.Headers.TryGetValues("X-Correlation-Id", out var correlationHeaderValues);
        var generatedId = correlationHeaderValues?.FirstOrDefault();

        Assert.False(string.IsNullOrWhiteSpace(generatedId));
        Assert.True(Guid.TryParse(generatedId, out _));
    }
}
