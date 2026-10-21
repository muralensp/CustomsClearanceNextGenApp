using CustomsClearance.Api.ConfigAdmin;
using CustomsClearance.Platform.Audit;
using CustomsClearance.Platform.RulesEngine;

namespace CustomsClearance.Api.RulesAdmin;

/// <summary>
/// Turns a rules admin action into an AuditEvent. Unlike config changes, a rule
/// change alters which clearance decisions the engine makes, so it is
/// BusinessAudit, classified Internal. The acting user is the JWT subject.
/// </summary>
internal static class RuleAudit
{
    public static Task WriteAsync(
        IAuditWriter auditWriter,
        HttpContext http,
        string action,
        Guid ruleId,
        IReadOnlyList<FieldChange> changes,
        CancellationToken cancellationToken)
    {
        var userId = ConfigAudit.CurrentUserId(http.User);
        var auditEvent = AuditEvent.Create(
            AuditEventCategory.BusinessAudit,
            entityType: "Rule",
            action: action,
            classification: DataClassification.Internal,
            entityId: ruleId,
            performedBy: userId,
            performedBySystem: userId is null ? "UnknownUser" : null,
            fieldChanges: changes);

        return auditWriter.WriteAsync(auditEvent, cancellationToken);
    }

    public static IReadOnlyList<FieldChange> VersionChanges(RuleVersion version, IReadOnlyCollection<RuleCondition> conditions) =>
    [
        new FieldChange(nameof(RuleVersion.VersionNumber), null, version.VersionNumber.ToString()),
        new FieldChange(nameof(RuleVersion.EffectiveFrom), null, version.EffectiveFrom.ToString("yyyy-MM-dd")),
        new FieldChange(nameof(RuleVersion.EffectiveTo), null, version.EffectiveTo?.ToString("yyyy-MM-dd")),
        new FieldChange(nameof(RuleVersion.OutcomeFactName), null, version.OutcomeFactName),
        new FieldChange(nameof(RuleVersion.OutcomeValue), null, version.OutcomeValue),
        new FieldChange("ConditionCount", null, conditions.Count.ToString()),
    ];
}
