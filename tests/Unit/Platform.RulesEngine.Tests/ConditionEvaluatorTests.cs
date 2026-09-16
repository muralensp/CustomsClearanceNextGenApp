using Xunit;

namespace CustomsClearance.Platform.RulesEngine.Tests;

public class ConditionEvaluatorTests
{
    private readonly ConditionEvaluator _evaluator = new();

    private static RuleCondition Condition(string factName, ConditionOperator op, string value) => new()
    {
        Id = Guid.NewGuid(),
        RuleVersionId = Guid.NewGuid(),
        FactName = factName,
        Operator = op,
        Value = value,
    };

    [Fact]
    public void IsSatisfied_MissingFact_ReturnsFalse()
    {
        var facts = new Facts();
        var condition = Condition("Missing", ConditionOperator.Equals, "x");

        Assert.False(_evaluator.IsSatisfied(condition, facts));
    }

    [Fact]
    public void IsSatisfied_Equals_StringMatch_ReturnsTrue()
    {
        var facts = new Facts { ["OriginCountry"] = "IN" };
        var condition = Condition("OriginCountry", ConditionOperator.Equals, "IN");

        Assert.True(_evaluator.IsSatisfied(condition, facts));
    }

    [Fact]
    public void IsSatisfied_Equals_StringMismatch_ReturnsFalse()
    {
        var facts = new Facts { ["OriginCountry"] = "PK" };
        var condition = Condition("OriginCountry", ConditionOperator.Equals, "IN");

        Assert.False(_evaluator.IsSatisfied(condition, facts));
    }

    [Fact]
    public void IsSatisfied_NotEquals_ReturnsTrueWhenDifferent()
    {
        var facts = new Facts { ["OriginCountry"] = "PK" };
        var condition = Condition("OriginCountry", ConditionOperator.NotEquals, "IN");

        Assert.True(_evaluator.IsSatisfied(condition, facts));
    }

    [Theory]
    [InlineData(4999.0, true)]
    [InlineData(5000.0, false)]
    [InlineData(5001.0, false)]
    public void IsSatisfied_LessThan_ComparesNumerically(double shipmentValue, bool expected)
    {
        var facts = new Facts { ["ShipmentValue"] = shipmentValue };
        var condition = Condition("ShipmentValue", ConditionOperator.LessThan, "5000");

        Assert.Equal(expected, _evaluator.IsSatisfied(condition, facts));
    }

    [Fact]
    public void IsSatisfied_GreaterThan_NumericStringDoesNotSortLexically()
    {
        // Lexical ("10" < "9") would wrongly fail this - proves numeric comparison is real.
        var facts = new Facts { ["ShipmentValue"] = 10m };
        var condition = Condition("ShipmentValue", ConditionOperator.GreaterThan, "9");

        Assert.True(_evaluator.IsSatisfied(condition, facts));
    }

    [Fact]
    public void IsSatisfied_GreaterThan_ComparesDatesNotStrings()
    {
        var facts = new Facts { ["ShipmentDate"] = new DateOnly(2026, 2, 1) };
        var condition = Condition("ShipmentDate", ConditionOperator.GreaterThan, "2026-01-15");

        Assert.True(_evaluator.IsSatisfied(condition, facts));
    }

    [Theory]
    [InlineData("IN", true)]
    [InlineData("PK", true)]
    [InlineData("US", false)]
    public void IsSatisfied_In_MatchesAnyListedValue(string origin, bool expected)
    {
        var facts = new Facts { ["OriginCountry"] = origin };
        var condition = Condition("OriginCountry", ConditionOperator.In, "IN, PK, BD");

        Assert.Equal(expected, _evaluator.IsSatisfied(condition, facts));
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(1.0, true)]
    [InlineData(2500.0, true)]
    [InlineData(5000.0, true)]
    [InlineData(5000.01, false)]
    public void IsSatisfied_Between_IsInclusiveOfBothBounds(double value, bool expected)
    {
        var facts = new Facts { ["ShipmentValue"] = value };
        var condition = Condition("ShipmentValue", ConditionOperator.Between, "1,5000");

        Assert.Equal(expected, _evaluator.IsSatisfied(condition, facts));
    }

    [Fact]
    public void IsSatisfied_Between_MalformedValue_ThrowsFormatException()
    {
        var facts = new Facts { ["ShipmentValue"] = 100m };
        var condition = Condition("ShipmentValue", ConditionOperator.Between, "1,5,10");

        Assert.Throws<FormatException>(() => _evaluator.IsSatisfied(condition, facts));
    }

    [Fact]
    public void IsSatisfied_Equals_NumericMatch_ComparesByValueNotText()
    {
        // ConditionValue "9.00" vs. actual 9m - equal by value, not by text.
        var facts = new Facts { ["ShipmentValue"] = 9m };
        var condition = Condition("ShipmentValue", ConditionOperator.Equals, "9.00");

        Assert.True(_evaluator.IsSatisfied(condition, facts));
    }

    [Fact]
    public void IsSatisfied_GreaterThan_NonComparableActual_FallsBackToOrdinalStringCompare()
    {
        // actual is a bool - not DateOnly, not numeric - so TryConvertToComparable
        // returns false and the ordinal string-compare fallback is exercised.
        var facts = new Facts { ["ControlledGoodsFlag"] = true };
        var condition = Condition("ControlledGoodsFlag", ConditionOperator.GreaterThan, "false");

        // "True" > "false" is false under ordinal comparison ('T' < 'f') - asserting the
        // actual (fallback) behavior, not a meaningful business comparison.
        Assert.False(_evaluator.IsSatisfied(condition, facts));
    }

    [Fact]
    public void IsSatisfied_Equals_DateActual_MalformedConditionValue_FallsBackToStringCompare()
    {
        var facts = new Facts { ["ShipmentDate"] = new DateOnly(2026, 2, 1) };
        var condition = Condition("ShipmentDate", ConditionOperator.Equals, "not-a-date");

        Assert.False(_evaluator.IsSatisfied(condition, facts));
    }

    [Fact]
    public void IsSatisfied_Equals_NumericActual_NonNumericConditionValue_FallsBackToStringCompare()
    {
        var facts = new Facts { ["ShipmentValue"] = 42m };
        var condition = Condition("ShipmentValue", ConditionOperator.Equals, "not-a-number");

        Assert.False(_evaluator.IsSatisfied(condition, facts));
    }

    [Fact]
    public void IsSatisfied_UnsupportedOperator_ThrowsNotSupportedException()
    {
        var facts = new Facts { ["X"] = "y" };
        var condition = Condition("X", (ConditionOperator)99, "y");

        Assert.Throws<NotSupportedException>(() => _evaluator.IsSatisfied(condition, facts));
    }
}
