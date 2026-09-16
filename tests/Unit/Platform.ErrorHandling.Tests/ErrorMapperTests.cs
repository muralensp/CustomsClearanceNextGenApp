using CustomsClearance.Platform.ErrorHandling;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CustomsClearance.Platform.ErrorHandling.Tests;

public class ErrorMapperTests
{
    [Fact]
    public void Map_BadHttpRequestException_ReturnsBadRequestNotInternalServerError()
    {
        // Day 34 finding: a malformed request body (e.g. an unparseable
        // JSON enum value) previously fell through to the generic 500
        // "unexpected error" branch, hiding a client-fixable mistake
        // behind a response that looked like a server bug.
        var exception = new BadHttpRequestException("Failed to read parameter \"CreateRuleRequest request\" from the request body as JSON.");

        var result = ErrorMapper.Map(exception, "/api/rules", "corr-3");

        Assert.Equal(400, result.Status);
        Assert.Equal("corr-3", result.CorrelationId);
        Assert.Equal("/api/rules", result.Instance);
        Assert.Null(result.Errors);
        Assert.Contains("could not be understood", result.Title);
    }

    [Fact]
    public void Map_DomainValidationException_ReturnsUnprocessableEntityWithBusinessRuleCode()
    {
        var exception = new DomainValidationException("BR-SHP-003", "Import shipments require an importer.", field: "importer");

        var result = ErrorMapper.Map(exception, "/api/v1/shipments/123", "corr-1");

        Assert.Equal(422, result.Status);
        Assert.Equal("corr-1", result.CorrelationId);
        Assert.Equal("/api/v1/shipments/123", result.Instance);
        Assert.NotNull(result.Errors);
        Assert.Single(result.Errors!);
        Assert.Equal("BR-SHP-003", result.Errors![0].Code);
        Assert.Equal("importer", result.Errors[0].Field);
    }

    [Fact]
    public void Map_DomainValidationExceptionWithMultipleFailures_ReturnsAllErrors()
    {
        var failures = new[]
        {
            new ValidationFailure("commodityId", "BR-COM-001", "HS Code is required."),
            new ValidationFailure("quantity", "COM-002", "Quantity must be greater than zero."),
        };
        var exception = new DomainValidationException(failures);

        var result = ErrorMapper.Map(exception, "/api/v1/commodities", null);

        Assert.Equal(2, result.Errors!.Count);
    }

    [Fact]
    public void Map_UnknownException_ReturnsInternalServerErrorWithoutLeakingDetail()
    {
        var exception = new InvalidOperationException("Connection string: Server=prod-db;Password=secret123");

        var result = ErrorMapper.Map(exception, "/api/v1/declarations/999", "corr-2");

        Assert.Equal(500, result.Status);
        Assert.Null(result.Detail);
        Assert.Null(result.Errors);
        Assert.DoesNotContain("secret123", result.Title);
        Assert.Equal("corr-2", result.CorrelationId);
    }
}
