using Microsoft.EntityFrameworkCore;

namespace CustomsClearance.Platform.Audit.Persistence;

public sealed record AuditQuery(
    string? EntityType = null,
    Guid? EntityId = null,
    Guid? PerformedBy = null,
    string? Action = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 50);

public sealed record AuditPage(int Total, int Page, int PageSize, IReadOnlyList<AuditEvent> Items);

/// <summary>
/// Read side of the audit trail: filtered, newest-first, paged. There is
/// deliberately no mutation method anywhere in this library.
/// </summary>
public sealed class AuditQueryService
{
    public const int MaxPageSize = 200;

    private readonly IDbContextFactory<AuditDbContext> _factory;

    public AuditQueryService(IDbContextFactory<AuditDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var records = db.AuditEvents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            records = records.Where(r => r.EntityType == query.EntityType);
        }

        if (query.EntityId is { } entityId)
        {
            records = records.Where(r => r.EntityId == entityId);
        }

        if (query.PerformedBy is { } performedBy)
        {
            records = records.Where(r => r.PerformedBy == performedBy);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            records = records.Where(r => r.Action == query.Action);
        }

        if (query.From is { } from)
        {
            records = records.Where(r => r.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            records = records.Where(r => r.OccurredAt <= to);
        }

        var total = await records.CountAsync(cancellationToken);
        var items = await records
            .OrderByDescending(r => r.OccurredAt)
            .ThenBy(r => r.AuditEventId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new AuditPage(total, page, pageSize, items.Select(r => r.ToEvent()).ToList());
    }

    public async Task<AuditEvent?> GetAsync(Guid auditEventId, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var record = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(r => r.AuditEventId == auditEventId, cancellationToken);
        return record?.ToEvent();
    }
}
