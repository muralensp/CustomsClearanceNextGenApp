using System.Security.Claims;
using CustomsClearance.Platform.Audit;

namespace CustomsClearance.Api.ConfigAdmin;

/// <summary>
/// Day 38: turns a config admin action into an AuditEvent. Config changes
/// are TechnicalAudit (AuditEventCategory's own doc lists "config change"),
/// classified Internal. The acting user is the JWT subject; the true old and
/// new values are stored as-is (masking is a display concern, FieldChange's
/// doc) - so secrets must never be kept in configuration values.
/// </summary>
internal static class ConfigAudit
{
    public static Task WriteAsync(
        IAuditWriter auditWriter,
        HttpContext http,
        string action,
        Guid entityId,
        IReadOnlyList<FieldChange> changes,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId(http.User);
        var auditEvent = AuditEvent.Create(
            AuditEventCategory.TechnicalAudit,
            entityType: "ConfigEntry",
            action: action,
            classification: DataClassification.Internal,
            entityId: entityId,
            performedBy: userId,
            performedBySystem: userId is null ? "UnknownUser" : null,
            fieldChanges: changes);

        return auditWriter.WriteAsync(auditEvent, cancellationToken);
    }

    /// <summary>The JWT "sub" claim (mapped to NameIdentifier by default) as a Guid.</summary>
    public static Guid? CurrentUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
