using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CustomsClearance.Platform.Logging.Tests;

/// <summary>
/// Exercises CorrelationIdMiddleware and UseCustomsClearanceCorrelationId
/// via a lightweight in-process TestServer pipeline (not a full
/// WebApplicationFactory - that's the Integration test layer per
/// adr/0009). This keeps the middleware's own coverage inside this unit
/// test project, since coverage thresholds are measured per-project.
/// </summary>
public class CorrelationIdMiddlewareTests
{
    private static async Task<(int StatusCode, string? ResponseHeader, string? ObservedCurrent)> InvokeAsync(string? requestHeaderValue)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.Configure(app =>
                {
                    app.UseCustomsClearanceCorrelationId();
                    app.Run(async context =>
                    {
                        context.Items["ObservedCurrent"] = CorrelationIdAccessor.Current;
                        await context.Response.WriteAsync("ok");
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        if (requestHeaderValue is not null)
        {
            client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, requestHeaderValue);
        }

        var response = await client.GetAsync("/");
        response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values);

        return (
            (int)response.StatusCode,
            values?.FirstOrDefault(),
            CorrelationIdAccessor.Current);
    }

    [Fact]
    public async Task WhenRequestSuppliesCorrelationId_EchoesSameIdOnResponse()
    {
        var suppliedId = "test-correlation-id-123";

        var (statusCode, responseHeader, _) = await InvokeAsync(suppliedId);

        Assert.Equal(200, statusCode);
        Assert.Equal(suppliedId, responseHeader);
    }

    [Fact]
    public async Task WhenDownstreamAlsoAddsTheHeader_ResponseStillCarriesExactlyOneValue()
    {
        // Simulates what YARP does behind the Gateway: it appends the proxied
        // service's own X-Correlation-Id response header. Day 41 fix: the client
        // must still see the header once.
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.Configure(app =>
                {
                    app.UseCustomsClearanceCorrelationId();
                    app.Run(async context =>
                    {
                        context.Response.Headers.Append(CorrelationIdMiddleware.HeaderName, "test-corr-dup");
                        await context.Response.WriteAsync("ok");
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, "test-corr-dup");

        var response = await client.GetAsync("/");

        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values));
        Assert.Equal(new[] { "test-corr-dup" }, values);
    }

    [Fact]
    public async Task WhenRequestOmitsCorrelationId_GeneratesOne()
    {
        var (statusCode, responseHeader, _) = await InvokeAsync(null);

        Assert.Equal(200, statusCode);
        Assert.False(string.IsNullOrWhiteSpace(responseHeader));
        Assert.True(Guid.TryParse(responseHeader, out _));
    }
}
