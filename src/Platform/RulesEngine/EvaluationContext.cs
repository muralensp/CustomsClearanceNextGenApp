namespace CustomsClearance.Platform.RulesEngine;

/// <summary>PRD Ch.5.4's evaluation subjects - open now to the two MVP types ADR-0013 named; extend as real bounded contexts (Shipment, Declaration, ...) are built.</summary>
public enum SubjectType
{
    Shipment,
    CourierShipment,
    Declaration,
}

/// <summary>
/// What is being evaluated (SubjectId/SubjectType, for IFactsProvider - Day
/// 32 does not yet have a real IFactsProvider implementation, since no real
/// Shipment/Declaration bounded context exists; Facts is supplied directly
/// to RulesEngine.EvaluateAsync by the caller for now) and when
/// (EvaluationDate, which RuleVersion effective-dating is matched against).
/// </summary>
public sealed record EvaluationContext(Guid SubjectId, SubjectType SubjectType, DateOnly EvaluationDate);
