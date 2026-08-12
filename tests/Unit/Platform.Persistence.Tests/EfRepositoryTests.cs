using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CustomsClearance.Platform.Persistence.Tests;

/// <summary>
/// Uses EF Core's InMemory provider - fast, no infrastructure required,
/// per adr/0009's "unit tests should not require infrastructure"
/// principle. Real PostgreSQL connectivity is proven separately (Day 18
/// manual verification against the docker-compose Postgres container, per
/// adr/0012), not duplicated here.
/// </summary>
public class EfRepositoryTests
{
    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    [Fact]
    public async Task AddAsync_ThenSaveChanges_StampsCreatedAt()
    {
        await using var context = CreateContext();
        var repository = new TestWidgetRepository(context);
        var widget = new TestWidget { Id = Guid.NewGuid(), Name = "Test" };

        var before = DateTimeOffset.UtcNow;
        await repository.AddAsync(widget);
        await context.SaveChangesAsync();
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(widget.CreatedAt, before, after);
        Assert.Null(widget.UpdatedAt);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsSavedEntity()
    {
        await using var context = CreateContext();
        var repository = new TestWidgetRepository(context);
        var id = Guid.NewGuid();
        await repository.AddAsync(new TestWidget { Id = id, Name = "Findable" });
        await context.SaveChangesAsync();

        var found = await repository.GetByIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Findable", found!.Name);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullWhenNotFound()
    {
        await using var context = CreateContext();
        var repository = new TestWidgetRepository(context);

        var found = await repository.GetByIdAsync(Guid.NewGuid());

        Assert.Null(found);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllSavedEntities()
    {
        await using var context = CreateContext();
        var repository = new TestWidgetRepository(context);
        await repository.AddAsync(new TestWidget { Id = Guid.NewGuid(), Name = "One" });
        await repository.AddAsync(new TestWidget { Id = Guid.NewGuid(), Name = "Two" });
        await context.SaveChangesAsync();

        var all = await repository.GetAllAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Update_ThenSaveChanges_StampsUpdatedAt()
    {
        await using var context = CreateContext();
        var repository = new TestWidgetRepository(context);
        var id = Guid.NewGuid();
        var widget = new TestWidget { Id = id, Name = "Original" };
        await repository.AddAsync(widget);
        await context.SaveChangesAsync();

        widget.Name = "Changed";
        repository.Update(widget);
        var before = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(widget.UpdatedAt!.Value, before, after);
    }

    [Fact]
    public async Task Remove_ThenSaveChanges_DeletesEntity()
    {
        await using var context = CreateContext();
        var repository = new TestWidgetRepository(context);
        var id = Guid.NewGuid();
        var widget = new TestWidget { Id = id, Name = "ToDelete" };
        await repository.AddAsync(widget);
        await context.SaveChangesAsync();

        repository.Remove(widget);
        await context.SaveChangesAsync();

        var found = await repository.GetByIdAsync(id);
        Assert.Null(found);
    }
}
