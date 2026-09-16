using System.Globalization;

namespace CustomsClearance.Platform.RulesEngine;

/// <summary>
/// Real implementation of the 6 operators ADR-0013 named (the Day 19
/// spike only implemented 4). A missing fact never satisfies a condition
/// (Equals/In/Between included) - absence is not a value, per PRD Ch.5.4's
/// "missing reference data" handling; a rule author who wants to match
/// "fact is absent" needs a different mechanism, not in scope here.
///
/// Numeric/date comparison, not just string comparison, for GreaterThan/
/// LessThan/Between: RuleCondition.Value is always text (Day 31 schema),
/// but a Fact's actual runtime value can be numeric (decimal ShipmentValue)
/// or a date - string-ordinal comparison of "9" vs "10" would wrongly
/// place "10" before "9". TryConvertToComparable normalizes both sides to
/// the same comparable type before comparing.
/// </summary>
public sealed class ConditionEvaluator : IConditionEvaluator
{
    public bool IsSatisfied(RuleCondition condition, Facts facts)
    {
        var actual = facts[condition.FactName];
        if (actual is null)
        {
            return false;
        }

        return condition.Operator switch
        {
            ConditionOperator.Equals => CompareEquality(actual, condition.Value),
            ConditionOperator.NotEquals => !CompareEquality(actual, condition.Value),
            ConditionOperator.GreaterThan => Compare(actual, condition.Value) > 0,
            ConditionOperator.LessThan => Compare(actual, condition.Value) < 0,
            ConditionOperator.In => IsIn(actual, condition.Value),
            ConditionOperator.Between => IsBetween(actual, condition.Value),
            _ => throw new NotSupportedException($"ConditionOperator {condition.Operator} is not supported."),
        };
    }

    private static bool CompareEquality(object actual, string conditionValue)
    {
        if (TryConvertToComparable(actual, conditionValue, out var actualComparable, out var conditionComparable))
        {
            return Comparer<object>.Default.Compare(actualComparable, conditionComparable) == 0;
        }

        return string.Equals(ToInvariantString(actual), conditionValue, StringComparison.Ordinal);
    }

    private static int Compare(object actual, string conditionValue)
    {
        if (TryConvertToComparable(actual, conditionValue, out var actualComparable, out var conditionComparable))
        {
            return Comparer<object>.Default.Compare(actualComparable, conditionComparable);
        }

        return string.CompareOrdinal(ToInvariantString(actual), conditionValue);
    }

    private static bool IsIn(object actual, string conditionValue)
    {
        var candidates = conditionValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return Array.Exists(candidates, candidate => CompareEquality(actual, candidate));
    }

    private static bool IsBetween(object actual, string conditionValue)
    {
        var parts = conditionValue.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            throw new FormatException($"Between condition value must be \"min,max\" - got \"{conditionValue}\".");
        }

        return Compare(actual, parts[0]) >= 0 && Compare(actual, parts[1]) <= 0;
    }

    /// <summary>Normalizes actual (a boxed runtime value) and conditionValue (always text) to the same comparable type - decimal for numerics, DateOnly for dates - so ordering compares magnitude, not lexical text.</summary>
    private static bool TryConvertToComparable(object actual, string conditionValue, out object actualComparable, out object conditionComparable)
    {
        switch (actual)
        {
            case DateOnly actualDate when DateOnly.TryParse(conditionValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var conditionDate):
                actualComparable = actualDate;
                conditionComparable = conditionDate;
                return true;

            case IConvertible when IsNumeric(actual) && decimal.TryParse(conditionValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var conditionDecimal):
                actualComparable = Convert.ToDecimal(actual, CultureInfo.InvariantCulture);
                conditionComparable = conditionDecimal;
                return true;

            default:
                actualComparable = actual;
                conditionComparable = conditionValue;
                return false;
        }
    }

    private static bool IsNumeric(object value) => value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static string ToInvariantString(object value) => value switch
    {
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
