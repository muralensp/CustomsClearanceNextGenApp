namespace CustomsClearance.Platform.Configuration.Tests;

public class ConfigValueValidatorTests
{
    [Theory]
    [InlineData(ConfigValueType.String, "anything", true)]
    [InlineData(ConfigValueType.String, "", true)]
    [InlineData(ConfigValueType.Integer, "5000", true)]
    [InlineData(ConfigValueType.Integer, "-3", true)]
    [InlineData(ConfigValueType.Integer, "12.5", false)]
    [InlineData(ConfigValueType.Integer, "abc", false)]
    [InlineData(ConfigValueType.Decimal, "12.5", true)]
    [InlineData(ConfigValueType.Decimal, "5000", true)]
    [InlineData(ConfigValueType.Decimal, "1,5x", false)]
    [InlineData(ConfigValueType.Boolean, "true", true)]
    [InlineData(ConfigValueType.Boolean, "False", true)]
    [InlineData(ConfigValueType.Boolean, "yes", false)]
    public void IsValid_ChecksValueAgainstDeclaredType(ConfigValueType type, string value, bool expected)
    {
        Assert.Equal(expected, ConfigValueValidator.IsValid(type, value));
    }

    [Fact]
    public void IsValid_NullValue_IsInvalid()
    {
        Assert.False(ConfigValueValidator.IsValid(ConfigValueType.String, null));
    }

    [Fact]
    public void IsValid_UnknownType_IsInvalid()
    {
        Assert.False(ConfigValueValidator.IsValid((ConfigValueType)999, "x"));
    }
}
