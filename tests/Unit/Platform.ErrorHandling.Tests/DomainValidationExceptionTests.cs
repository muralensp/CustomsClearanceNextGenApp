using CustomsClearance.Platform.ErrorHandling;
using Xunit;

namespace CustomsClearance.Platform.ErrorHandling.Tests;

public class DomainValidationExceptionTests
{
    [Fact]
    public void SingleFailureConstructor_PopulatesOneFailure()
    {
        var ex = new DomainValidationException("BR-DEC-004", "Required permit not approved.");

        Assert.Single(ex.Failures);
        Assert.Equal("BR-DEC-004", ex.Failures[0].Code);
        Assert.Null(ex.Failures[0].Field);
    }

    [Fact]
    public void MultiFailureConstructor_RequiresAtLeastOneFailure()
    {
        Assert.Throws<ArgumentException>(() => new DomainValidationException(Array.Empty<ValidationFailure>()));
    }

    [Fact]
    public void MultiFailureConstructor_MessageSummarizesCount()
    {
        var failures = new[]
        {
            new ValidationFailure("a", "CODE-1", "First failure"),
            new ValidationFailure("b", "CODE-2", "Second failure"),
        };

        var ex = new DomainValidationException(failures);

        Assert.Contains("2", ex.Message);
    }
}
