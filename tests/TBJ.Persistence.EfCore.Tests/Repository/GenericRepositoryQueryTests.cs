using TBJ.Persistence.EfCore.Implementation;
using TBJ.Persistence.EfCore.Tests.Helpers;

namespace TBJ.Persistence.EfCore.Tests.Repository;

/// <summary>Unit tests for <see cref="GenericRepository{TEntity}"/> — AsQueryable and advanced query composition.</summary>
public class GenericRepositoryQueryTests
{
    [Fact]
    public async Task AsQueryable_WithOrderBy_ReturnsSortedResults()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        repo.Insert([
            new TestEntity { Id = 3, Name = "C", Value = 30 },
            new TestEntity { Id = 1, Name = "A", Value = 10 },
            new TestEntity { Id = 2, Name = "B", Value = 20 }
        ]);
        await ctx.SaveChangesAsync();

        // Act
        var result = await repo.GetAsync(
            orderBy: q => q.OrderBy(e => e.Value));

        // Assert
        Assert.Equal([10, 20, 30], result.Select(e => e.Value));
    }

    [Fact]
    public async Task AsQueryable_NoTracking_ChangeTrackerEmpty()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        repo.Insert(new TestEntity { Id = 1, Name = "Tracked" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Act — default tracking=false
        await repo.GetAsync();

        // Assert — nothing tracked after read-only query
        Assert.Empty(ctx.ChangeTracker.Entries());
    }

    // ExecuteDeleteAsync wymaga dostawcy relacyjnego — InMemory nie jest obsługiwany.
    // Test masowego usuwania znajduje się w projekcie integracyjnym (SQLite).
    [Fact(Skip = "ExecuteDeleteAsync wymaga relacyjnego dostawcy bazy danych — niedostępny w dostawcy InMemory.")]
    public async Task DeleteRangeAsync_RemovesMatchingEntities()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        var repo = new GenericRepository<TestEntity>(ctx);
        repo.Insert([
            new TestEntity { Id = 1, Value = 1 },
            new TestEntity { Id = 2, Value = 2 },
            new TestEntity { Id = 3, Value = 1 }
        ]);
        await ctx.SaveChangesAsync();

        // Act
        var deleted = await repo.DeleteRangeAsync(e => e.Value == 1);

        // Assert
        Assert.Equal(2, deleted);
        Assert.Equal(1, await repo.CountAsync());
    }
}
