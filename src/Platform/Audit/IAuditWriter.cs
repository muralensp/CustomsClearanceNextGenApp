namespace CustomsClearance.Platform.Audit;

/// <summary>
/// Port for persisting audit events. Kept deliberately storage-agnostic
/// (per adr/0006's Clean Architecture adaptation - this Platform library
/// defines the port, an Infrastructure-tier implementation provides the
/// real adapter) since the real implementation (append-only PostgreSQL
/// table, per adr/0004 Section 9) isn't built until EF Core is wired up
/// for a specific context in Phase 0/3. ConsoleAuditWriter (this library)
/// is a functional placeholder default, not the production implementation.
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
