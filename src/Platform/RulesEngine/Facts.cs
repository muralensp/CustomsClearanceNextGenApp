namespace CustomsClearance.Platform.RulesEngine;

/// <summary>
/// PRD Ch.5.4-shaped fact bag: FactName (an open string, per RuleCondition
/// .FactName - not an enum) -> value, boxed as object but constrained to
/// the same primitive set RuleCondition.Value already assumes
/// (string/number/bool/date). Per ADR-0013, deliberately mutable and
/// mutated in place as the pipeline progresses (RulesEngine.ApplyOutcome
/// below) - later PRD Ch.5.7 stages (e.g. Permit) depend on earlier
/// stages' outcomes (e.g. HS Code setting "ControlledGoodsFlag"), so a
/// frozen snapshot assembled once at pipeline start cannot express this.
/// </summary>
public sealed class Facts
{
    private readonly Dictionary<string, object?> _values = new();

    public object? this[string factName]
    {
        get => _values.GetValueOrDefault(factName);
        set => _values[factName] = value;
    }

    /// <summary>Immutable point-in-time copy - used by IDecisionTraceRecorder (Day 33) so a trace step's snapshot survives later mutation.</summary>
    public IReadOnlyDictionary<string, object?> Snapshot() => new Dictionary<string, object?>(_values);
}
