using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Platform.Audit.Persistence;

/// <summary>
/// The durable IAuditWriter. A singleton (like the ConsoleAuditWriter it
/// replaces), so it takes a DbContext factory and uses a short-lived context
/// per write - on its own connection, i.e. an audit write is NOT part of the
/// caller's database transaction (the sequential-write trade-off recorded in
/// ADR-0032). A failed write throws; callers must not swallow it.
/// </summary>
public sealed class EfAuditWriter : IAuditWriter
{
    private readonly IDbContextFactory<AuditDbContext> _factory;

    public EfAuditWriter(IDbContextFactory<AuditDbContext> factory)
    {
        _factory = factory;
    }

    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        db.AuditEvents.Add(AuditEventRecord.From(auditEvent));
        await db.SaveChangesAsync(cancellationToken);
    }
}
