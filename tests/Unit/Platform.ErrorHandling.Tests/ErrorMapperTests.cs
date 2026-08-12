using CustomsClearance.Platform.ErrorHandling;
using Xunit;

namespace CustomsClearance.Platform.ErrorHandling.Tests;

public class ErrorMapperTests
{
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
