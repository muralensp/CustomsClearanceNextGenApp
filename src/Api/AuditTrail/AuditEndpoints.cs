using CustomsClearance.Platform.Audit;
using CustomsClearance.Platform.Audit.Persistence;
using CustomsClearance.Platform.Security;

namespace CustomsClearance.Api.AuditTrail;

public sealed record AuditEventResponse(
    Guid AuditEventId,
    string? CorrelationId,
    string Category,
    string EntityType,
    Guid? EntityId,
    string Action,
    Guid? PerformedBy,
    string? PerformedBySystem,
    DateTimeOffset OccurredAt,
    string? BusinessRuleRef,
    string Classification,
    IReadOnlyList<FieldChange> FieldChanges);

public sealed record AuditPageResponse(int Total, int Page, int PageSize, IReadOnlyList<AuditEventResponse> Items);

/// <summary>
/// Day 38's read API for the audit trail (Compliance Officer or Admin). Read
/// only by design - there is no create/update/delete endpoint, and the audit
/// store itself rejects mutation. Results are newest-first and paged.
/// </summary>
public static class AuditEndpoints
{
    public static WebApplication MapAuditEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/audit").RequireAuthorization(PlatformPolicies.RequireComplianceOfficerOrAdmin);

        group.MapGet("/", QueryAsync).WithName("QueryAuditTrail");
        group.MapGet("/{auditEventId:guid}", GetAsync).WithName("GetAuditEvent");

        return app;
    }

    private static async Task<IResult> QueryAsync(
        AuditQueryService queryService,
        string? entityType,
        Guid? entityId,
        Guid? performedBy,
        string? action,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await queryService.QueryAsync(
            new AuditQuery(entityType, entityId, performedBy, action, from, to, page ?? 1, pageSize ?? 50),
            cancellationToken);

        return Results.Ok(new AuditPageResponse(result.Total, result.Page, result.PageSize, result.Items.Select(ToResponse).ToList()));
    }

    private static async Task<IResult> GetAsync(Guid auditEventId, AuditQueryService queryService, CancellationToken cancellationToken)
    {
        var auditEvent = await queryService.GetAsync(auditEventId, cancellationToken);
        return auditEvent is null ? Results.NotFound() : Results.Ok(ToResponse(auditEvent));
    }

    private static AuditEventResponse ToResponse(AuditEvent e) => new(
        e.AuditEventId,
        e.CorrelationId,
        e.EventCategory.ToString(),
        e.EntityType,
        e.EntityId,
        e.Action,
        e.PerformedBy,
        e.PerformedBySystem,
        e.OccurredAt,
        e.BusinessRuleRef,
        e.Classification.ToString(),
        e.FieldChanges ?? Array.Empty<FieldChange>());
}
