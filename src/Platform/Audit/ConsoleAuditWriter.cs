using Microsoft.Extensions.Logging;

namespace CustomsClearance.Platform.Audit;

/// <summary>
/// Default IAuditWriter: writes each audit event as a structured log line.
/// Functional (not a no-op) so the audit trail actually exists and is
/// visible today, but explicitly a placeholder for the real append-only
/// database sink (adr/0004 Section 9) - see IAuditWriter's doc comment.
/// </summary>
public sealed class ConsoleAuditWriter : IAuditWriter
{
    private readonly ILogger<ConsoleAuditWriter> _logger;

    public ConsoleAuditWriter(ILogger<ConsoleAuditWriter> logger)
    {
        _logger = logger;
    }

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "AUDIT [{Category}] {Action} on {EntityType} {EntityId} by {PerformedBy} " +
            "(CorrelationId={CorrelationId}, BusinessRuleRef={BusinessRuleRef}, Classification={Classification})",
            auditEvent.EventCategory,
            auditEvent.Action,
            auditEvent.EntityType,
            auditEvent.EntityId,
            auditEvent.PerformedBy?.ToString() ?? auditEvent.PerformedBySystem,
            auditEvent.CorrelationId,
            auditEvent.BusinessRuleRef,
            auditEvent.Classification);

        return Task.CompletedTask;
    }
}
