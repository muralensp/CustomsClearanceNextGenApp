using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CustomsClearance.Platform.Audit.Persistence.Tests;

internal sealed class TestFactory : IDbContextFactory<AuditDbContext>
{
    private readonly DbContextOptions<AuditDbContext> _options;

    public TestFactory(string name)
    {
        _options = new DbContextOptionsBuilder<AuditDbContext>().UseInMemoryDatabase(name).Options;
    }

    public AuditDbContext CreateDbContext() => new(_options);
}

internal static class Events
{
    public static AuditEvent Make(
        string entityType = "ConfigEntry",
        Guid? entityId = null,
        Guid? performedBy = null,
        string action = "ConfigUpdated",
        IReadOnlyList<FieldChange>? changes = null) =>
        AuditEvent.Create(
            AuditEventCategory.TechnicalAudit,
            entityType,
            action,
            DataClassification.Internal,
            entityId: entityId,
            performedBy: performedBy ?? Guid.NewGuid(),
            businessRuleRef: "REF-1",
            fieldChanges: changes);
}

public class EfAuditWriterTests
{
    [Fact]
    public async Task WriteAsync_RoundTripsEveryFieldIncludingOldAndNewValues()
    {
        var factory = new TestFactory(nameof(WriteAsync_RoundTripsEveryFieldIncludingOldAndNewValues));
        var user = Guid.NewGuid();
        var entity = Guid.NewGuid();
        var original = Events.Make(entityId: entity, performedBy: user, changes: new[] { new FieldChange("Value", "5000", "10000") });

        await new EfAuditWriter(factory).WriteAsync(original);

        var read = await new AuditQueryService(factory).GetAsync(original.AuditEventId);
        Assert.NotNull(read);
        Assert.Equal(original.AuditEventId, read.AuditEventId);
        Assert.Equal(original.CorrelationId, read.CorrelationId);
        Assert.Equal(AuditEventCategory.TechnicalAudit, read.EventCategory);
        Assert.Equal("ConfigEntry", read.EntityType);
        Assert.Equal(entity, read.EntityId);
        Assert.Equal("ConfigUpdated", read.Action);
        Assert.Equal(user, read.PerformedBy);
        Assert.Equal(original.OccurredAt, read.OccurredAt);
        Assert.Equal("REF-1", read.BusinessRuleRef);
        Assert.Equal(DataClassification.Internal, read.Classification);
        var change = Assert.Single(read.FieldChanges!);
        Assert.Equal(new FieldChange("Value", "5000", "10000"), change);
    }

    [Fact]
    public async Task WriteAsync_WithoutFieldChanges_ReadsBackNull()
    {
        var factory = new TestFactory(nameof(WriteAsync_WithoutFieldChanges_ReadsBackNull));
        var evt = Events.Make();

        await new EfAuditWriter(factory).WriteAsync(evt);

        Assert.Null((await new AuditQueryService(factory).GetAsync(evt.AuditEventId))!.FieldChanges);
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        var factory = new TestFactory(nameof(GetAsync_UnknownId_ReturnsNull));

        Assert.Null(await new AuditQueryService(factory).GetAsync(Guid.NewGuid()));
    }
}

public class AppendOnlyTests
{
    [Fact]
    public async Task ModifyingAStoredEvent_IsRefused_AndRowIsUnchanged()
    {
        var factory = new TestFactory(nameof(ModifyingAStoredEvent_IsRefused_AndRowIsUnchanged));
        var evt = Events.Make(action: "Original");
        await new EfAuditWriter(factory).WriteAsync(evt);

        await using (var db = factory.CreateDbContext())
        {
            var record = await db.AuditEvents.SingleAsync();
            record.Action = "Tampered";

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Contains("append-only", ex.Message);
        }

        Assert.Equal("Original", (await new AuditQueryService(factory).GetAsync(evt.AuditEventId))!.Action);
    }

    [Fact]
    public async Task DeletingAStoredEvent_IsRefused_AndRowRemains()
    {
        var factory = new TestFactory(nameof(DeletingAStoredEvent_IsRefused_AndRowRemains));
        var evt = Events.Make();
        await new EfAuditWriter(factory).WriteAsync(evt);

        await using (var db = factory.CreateDbContext())
        {
            db.AuditEvents.Remove(await db.AuditEvents.SingleAsync());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Contains("delete", ex.Message);
        }

        Assert.NotNull(await new AuditQueryService(factory).GetAsync(evt.AuditEventId));
    }

    [Fact]
    public async Task SynchronousSaveChanges_AlsoRefusesMutation()
    {
        var factory = new TestFactory(nameof(SynchronousSaveChanges_AlsoRefusesMutation));
        await new EfAuditWriter(factory).WriteAsync(Events.Make());

        using var db = factory.CreateDbContext();
        (await db.AuditEvents.SingleAsync()).Action = "Tampered";

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public void SynchronousSaveChanges_StillAllowsAppending()
    {
        var factory = new TestFactory(nameof(SynchronousSaveChanges_StillAllowsAppending));

        using var db = factory.CreateDbContext();
        db.AuditEvents.Add(AuditEventRecord.From(Events.Make()));

        Assert.Equal(1, db.SaveChanges());
    }
}

public class AuditQueryServiceTests
{
    private static async Task<(TestFactory Factory, Guid EntityA, Guid UserA, Guid UserB)> SeedAsync(string name)
    {
        var factory = new TestFactory(name);
        var writer = new EfAuditWriter(factory);
        var entityA = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await writer.WriteAsync(Events.Make("ConfigEntry", entityA, userA, "ConfigCreated"));
        await Task.Delay(5);
        await writer.WriteAsync(Events.Make("ConfigEntry", entityA, userB, "ConfigUpdated"));
        await Task.Delay(5);
        await writer.WriteAsync(Events.Make("Notification", Guid.NewGuid(), userA, "Sent"));
        return (factory, entityA, userA, userB);
    }

    [Fact]
    public async Task Query_ReturnsNewestFirst()
    {
        var (factory, _, _, _) = await SeedAsync(nameof(Query_ReturnsNewestFirst));

        var page = await new AuditQueryService(factory).QueryAsync(new AuditQuery());

        Assert.Equal(3, page.Total);
        Assert.Equal(new[] { "Sent", "ConfigUpdated", "ConfigCreated" }, page.Items.Select(i => i.Action).ToArray());
    }

    [Fact]
    public async Task Query_FiltersByEntityTypeAndEntityId()
    {
        var (factory, entityA, _, _) = await SeedAsync(nameof(Query_FiltersByEntityTypeAndEntityId));
        var service = new AuditQueryService(factory);

        Assert.Equal(2, (await service.QueryAsync(new AuditQuery(EntityType: "ConfigEntry"))).Total);
        Assert.Equal(2, (await service.QueryAsync(new AuditQuery(EntityId: entityA))).Total);
        Assert.Equal(0, (await service.QueryAsync(new AuditQuery(EntityType: "Nope"))).Total);
    }

    [Fact]
    public async Task Query_FiltersByPerformerAndAction()
    {
        var (factory, _, userA, userB) = await SeedAsync(nameof(Query_FiltersByPerformerAndAction));
        var service = new AuditQueryService(factory);

        Assert.Equal(2, (await service.QueryAsync(new AuditQuery(PerformedBy: userA))).Total);
        Assert.Equal(1, (await service.QueryAsync(new AuditQuery(PerformedBy: userB))).Total);
        Assert.Equal(1, (await service.QueryAsync(new AuditQuery(Action: "ConfigCreated"))).Total);
    }

    [Fact]
    public async Task Query_FiltersByTimeWindow()
    {
        var (factory, _, _, _) = await SeedAsync(nameof(Query_FiltersByTimeWindow));
        var service = new AuditQueryService(factory);
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(3, (await service.QueryAsync(new AuditQuery(From: now.AddMinutes(-5), To: now.AddMinutes(5)))).Total);
        Assert.Equal(0, (await service.QueryAsync(new AuditQuery(From: now.AddMinutes(5)))).Total);
        Assert.Equal(0, (await service.QueryAsync(new AuditQuery(To: now.AddMinutes(-5)))).Total);
    }

    [Fact]
    public async Task Query_PagesResults_AndClampsBadPaging()
    {
        var (factory, _, _, _) = await SeedAsync(nameof(Query_PagesResults_AndClampsBadPaging));
        var service = new AuditQueryService(factory);

        var second = await service.QueryAsync(new AuditQuery(Page: 2, PageSize: 2));
        Assert.Equal(3, second.Total);
        Assert.Equal("ConfigCreated", Assert.Single(second.Items).Action);

        var clamped = await service.QueryAsync(new AuditQuery(Page: -4, PageSize: 100000));
        Assert.Equal(1, clamped.Page);
        Assert.Equal(AuditQueryService.MaxPageSize, clamped.PageSize);
    }
}

public class RegistrationTests
{
    [Fact]
    public void AddCustomsClearanceAuditPersistence_ReplacesConsoleWriterWithDurableWriter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCustomsClearanceAudit();
        Assert.IsType<ConsoleAuditWriter>(services.BuildServiceProvider().GetRequiredService<IAuditWriter>());

        services.AddCustomsClearanceAuditPersistence(o => o.UseInMemoryDatabase(nameof(AddCustomsClearanceAuditPersistence_ReplacesConsoleWriterWithDurableWriter)));
        using var provider = services.BuildServiceProvider();

        Assert.IsType<EfAuditWriter>(provider.GetRequiredService<IAuditWriter>());
        Assert.NotNull(provider.GetRequiredService<AuditQueryService>());
    }
}
