using System.Collections.Concurrent;

namespace CustomsClearance.Platform.Audit;

/// <summary>
/// Captures audit events in memory instead of writing them anywhere
/// external - intended for unit/integration tests that need to assert an
/// audit event was actually raised, not for any real environment.
/// </summary>
public sealed class InMemoryAuditWriter : IAuditWriter
{
    private readonly ConcurrentQueue<AuditEvent> _events = new();

    public IReadOnlyCollection<AuditEvent> Events => _events.ToArray();

    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }
}
