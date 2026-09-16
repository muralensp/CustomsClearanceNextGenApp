using System.Text.Json;
using CustomsClearance.Platform.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CustomsClearance.Platform.ErrorHandling.Tests;

/// <summary>
/// Exercises ExceptionHandlingMiddleware via an in-process TestServer
/// pipeline (unit-test-layer, per adr/0009 - not the WebApplicationFactory
/// integration layer), including its interaction with
/// Platform.Logging's correlation ID middleware, since the two are
/// designed to compose together in a real Api host.
/// </summary>
public class ExceptionHandlingMiddlewareTests
{
    private static async Task<(int StatusCode, string Body, string? CorrelationHeader)> InvokeAsync(Action<Microsoft.AspNetCore.Http.HttpContext> action, string? requestCorrelationId = null)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.Configure(app =>
                {
                    app.UseCustomsClearanceCorrelationId();
                    app.UseCustomsClearanceErrorHandling();
                    app.Run(context =>
                    {
                        action(context);
                        return Task.CompletedTask;
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        if (requestCorrelationId is not null)
        {
            client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, requestCorrelationId);
        }

        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();
        response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values);

        return ((int)response.StatusCode, body, values?.FirstOrDefault());
    }

    [Fact]
    public async Task DomainValidationException_MapsToUnprocessableEntityWithCorrelationId()
    {
        var (statusCode, body, correlationHeader) = await InvokeAsync(
            _ => throw new DomainValidationException("BR-DEC-004", "Required permit not approved."),
            requestCorrelationId: "test-corr-abc");

        Assert.Equal(422, statusCode);
        Assert.Equal("test-corr-abc", correlationHeader);

        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal("test-corr-abc", parsed.GetProperty("correlationId").GetString());
        Assert.Equal("BR-DEC-004", parsed.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task BadHttpRequestException_MapsToBadRequestNotInternalServerError()
    {
        var (statusCode, body, correlationHeader) = await InvokeAsync(
            _ => throw new Microsoft.AspNetCore.Http.BadHttpRequestException("malformed body"),
            requestCorrelationId: "test-corr-badreq");

        Assert.Equal(400, statusCode);
        Assert.Equal("test-corr-badreq", correlationHeader);

        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.Equal("test-corr-badreq", parsed.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task UnknownException_MapsToInternalServerErrorWithoutLeakingMessage()
    {
        var (statusCode, body, _) = await InvokeAsync(
            _ => throw new InvalidOperationException("sensitive internal detail"));

        Assert.Equal(500, statusCode);
        Assert.DoesNotContain("sensitive internal detail", body);
    }

    [Fact]
    public async Task NoException_PassesThroughUnaffected()
    {
        var (statusCode, _, _) = await InvokeAsync(context => context.Response.StatusCode = 200);

        Assert.Equal(200, statusCode);
    }
}
