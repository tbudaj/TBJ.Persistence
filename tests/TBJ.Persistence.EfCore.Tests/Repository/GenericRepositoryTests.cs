using TBJ.Persistence.EfCore.Implementation;
using TBJ.Persistence.EfCore.Tests.Helpers;

namespace TBJ.Persistence.EfCore.Tests.Repository;

/// <summary>Unit tests for <see cref="GenericRepository{TEntity}"/> — CRUD and query operations.</summary>
public class GenericRepositoryTests
{
    // -----------------------------------------------------------------------
    // Insert + FindAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Insert_SingleEntity_FindAsyncReturnsIt()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        var entity = new TestEntity { Id = 1, Name = "Alpha" };

        // Act
        repo.Insert(entity);
        await ctx.SaveChangesAsync();

        // Assert
        var found = await repo.FindAsync(1);
        Assert.NotNull(found);
        Assert.Equal("Alpha", found.Name);
    }

    [Fact]
    public async Task Insert_Collection_AllEntitiesArePersisted()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        var entities = Enumerable.Range(1, 5).Select(i => new TestEntity { Id = i, Name = $"Entity{i}" });

        // Act
        repo.Insert(entities);
        await ctx.SaveChangesAsync();

        // Assert
        var list = await repo.GetAsync();
        Assert.Equal(5, list.Count);
    }

    // -----------------------------------------------------------------------
    // GetAsync with filter
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_WithFilter_ReturnsMatchingEntities()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        repo.Insert([
            new TestEntity { Id = 1, Name = "Match", Value = 10 },
            new TestEntity { Id = 2, Name = "NoMatch", Value = 20 },
            new TestEntity { Id = 3, Name = "Match2", Value = 10 }
        ]);
        await ctx.SaveChangesAsync();

        // Act
        var result = await repo.GetAsync(filter: e => e.Value == 10);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal(10, e.Value));
    }

    [Fact]
    public async Task GetAsync_WithPaging_ReturnsCorrectPage()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        repo.Insert(Enumerable.Range(1, 10).Select(i => new TestEntity { Id = i, Name = $"E{i}" }));
        await ctx.SaveChangesAsync();

        // Act — page 2, page size 3
        var result = await repo.GetAsync(skip: 3, take: 3);

        // Assert
        Assert.Equal(3, result.Count);
    }

    // -----------------------------------------------------------------------
    // ExistsAsync / CountAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExistsAsync_WithMatchingPredicate_ReturnsTrue()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        repo.Insert(new TestEntity { Id = 1, Name = "Test" });
        await ctx.SaveChangesAsync();

        // Act & Assert
        Assert.True(await repo.ExistsAsync(e => e.Name == "Test"));
        Assert.False(await repo.ExistsAsync(e => e.Name == "Missing"));
    }

    [Fact]
    public async Task CountAsync_WithPredicate_ReturnsCorrectCount()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        repo.Insert([
            new TestEntity { Id = 1, Value = 5 },
            new TestEntity { Id = 2, Value = 10 },
            new TestEntity { Id = 3, Value = 5 }
        ]);
        await ctx.SaveChangesAsync();

        // Act
        int count = await repo.CountAsync(e => e.Value == 5);

        // Assert
        Assert.Equal(2, count);
    }

    // -----------------------------------------------------------------------
    // FirstOrDefaultAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FirstOrDefaultAsync_ExistingEntity_ReturnsEntity()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        repo.Insert(new TestEntity { Id = 1, Name = "First" });
        await ctx.SaveChangesAsync();

        // Act
        var result = await repo.FirstOrDefaultAsync(e => e.Id == 1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("First", result.Name);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_MissingEntity_ReturnsNull()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);

        // Act
        var result = await repo.FirstOrDefaultAsync(e => e.Id == 99);

        // Assert
        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // Update
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Update_ChangesArePersisted()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        repo.Insert(new TestEntity { Id = 1, Name = "Original" });
        await ctx.SaveChangesAsync();

        // Act
        var entity = await repo.FindAsync(1);
        entity!.Name = "Updated";
        repo.Update(entity);
        await ctx.SaveChangesAsync();

        // Assert
        var updated = await repo.FindAsync(1);
        Assert.Equal("Updated", updated!.Name);
    }

    // -----------------------------------------------------------------------
    // Delete
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Delete_SingleEntity_RemovedFromDatabase()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        var entity = new TestEntity { Id = 1, Name = "ToDelete" };
        repo.Insert(entity);
        await ctx.SaveChangesAsync();

        // Act
        repo.Delete(entity);
        await ctx.SaveChangesAsync();

        // Assert
        Assert.Null(await repo.FindAsync(1));
    }

    // -----------------------------------------------------------------------
    // InsertOrUpdateAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InsertOrUpdateAsync_NewEntity_Inserted()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);

        // Act
        await repo.InsertOrUpdateAsync(new TestEntity { Id = 1, Name = "New" });
        await ctx.SaveChangesAsync();

        // Assert
        Assert.NotNull(await repo.FindAsync(1));
    }

    [Fact]
    public async Task InsertOrUpdateAsync_ExistingEntity_Updated()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        repo.Insert(new TestEntity { Id = 1, Name = "Original" });
        await ctx.SaveChangesAsync();

        // Act
        await repo.InsertOrUpdateAsync(new TestEntity { Id = 1, Name = "Changed" });
        await ctx.SaveChangesAsync();

        // Assert
        var result = await repo.FindAsync(1);
        Assert.Equal("Changed", result!.Name);
    }

    // -----------------------------------------------------------------------
    // InsertNewAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InsertNewAsync_SkipsExistingEntities()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        repo.Insert(new TestEntity { Id = 1, Name = "Existing" });
        await ctx.SaveChangesAsync();

        // Act
        await repo.InsertNewAsync([
            new TestEntity { Id = 1, Name = "ShouldBeSkipped" },
            new TestEntity { Id = 2, Name = "ShouldBeInserted" }
        ]);
        await ctx.SaveChangesAsync();

        // Assert
        var all = await repo.GetAsync();
        Assert.Equal(2, all.Count);
        // Existing entity must not be overwritten
        Assert.Equal("Existing", all.First(e => e.Id == 1).Name);
    }

    // -----------------------------------------------------------------------
    // Attach / Detach
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Attach_EntityIsTracked()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        repo.Insert(new TestEntity { Id = 1, Name = "A" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Act
        var entity = new TestEntity { Id = 1, Name = "A" };
        repo.Attach(entity);

        // Assert — entity is tracked after attach
        Assert.NotEqual(Microsoft.EntityFrameworkCore.EntityState.Detached, ctx.Entry(entity).State);
    }

    [Fact]
    public async Task Detach_EntityIsNoLongerTracked()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        var entity = new TestEntity { Id = 1, Name = "B" };
        repo.Insert(entity);
        await ctx.SaveChangesAsync();

        // Act
        repo.Detach(entity);

        // Assert
        Assert.Equal(Microsoft.EntityFrameworkCore.EntityState.Detached, ctx.Entry(entity).State);
    }
}
