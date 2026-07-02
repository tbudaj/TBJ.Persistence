using TBJ.Persistence.EfCore.Abstractions;
using TBJ.Persistence.EfCore.Tests.Helpers;

namespace TBJ.Persistence.EfCore.Tests.UnitOfWork;

/// <summary>Unit tests for <see cref="IGenericUnitOfWork"/> — SaveChanges, repository access and transactions.</summary>
public class GenericUnitOfWorkTests
{
    [Fact]
    public async Task SaveChangesAsync_PersistsInsertedEntities()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        using var uow = new TestUnitOfWork(ctx);
        var repo = uow.Repository<TestEntity>();
        repo.Insert(new TestEntity { Id = 1, Name = "Saved" });

        // Act
        int saved = await uow.SaveChangesAsync();

        // Assert
        Assert.Equal(1, saved);
        Assert.NotNull(await repo.FindAsync(1));
    }

    [Fact]
    public async Task Repository_SameType_ReturnsCachedInstance()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        using var uow = new TestUnitOfWork(ctx);

        // Act
        var repo1 = uow.Repository<TestEntity>();
        var repo2 = uow.Repository<TestEntity>();

        // Assert — same instance from cache
        Assert.Same(repo1, repo2);
    }

    [Fact]
    public void Repository_UnregisteredEntityType_ThrowsInvalidOperationException()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        using var uow = new TestUnitOfWork(ctx);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => uow.Repository<UnregisteredEntity>());
    }

    [Fact]
    public async Task Context_ExposesUnderlyingDbContext()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        using var uow = new TestUnitOfWork(ctx);

        // Assert
        Assert.NotNull(uow.Context);
        Assert.IsType<TestDbContext>(uow.Context);
    }

    [Fact]
    public async Task BeginTransactionAsync_ThenCommit_ChangesArePersisted()
    {
        // Arrange — InMemory does not support real transactions but the API must not throw
        using var ctx = InMemoryDbContextFactory.Create();
        using var uow = new TestUnitOfWork(ctx);
        var repo = uow.Repository<TestEntity>();
        repo.Insert(new TestEntity { Id = 1, Name = "TX" });

        // Act — InMemory provider ignores transactions; we verify the API contract
        await uow.BeginTransactionAsync();
        await uow.SaveChangesAsync();
        await uow.CommitTransactionAsync();

        // Assert
        Assert.NotNull(await repo.FindAsync(1));
    }

    [Fact]
    public async Task BeginTransactionAsync_CalledTwice_SecondCallIsNoop()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        using var uow = new TestUnitOfWork(ctx);

        // Act — second call must not throw
        await uow.BeginTransactionAsync();
        var ex = await Record.ExceptionAsync(() => uow.BeginTransactionAsync());

        // Assert
        Assert.Null(ex);
    }

    [Fact]
    public async Task CommitTransactionAsync_WithNoActiveTransaction_IsNoop()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        using var uow = new TestUnitOfWork(ctx);

        // Act & Assert — must not throw
        var ex = await Record.ExceptionAsync(() => uow.CommitTransactionAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task RollbackTransactionAsync_WithNoActiveTransaction_IsNoop()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        using var uow = new TestUnitOfWork(ctx);

        // Act & Assert — must not throw
        var ex = await Record.ExceptionAsync(() => uow.RollbackTransactionAsync());
        Assert.Null(ex);
    }

    [Fact]
    public void ClearChangeTracker_DetachesAllEntities()
    {
        // Arrange
        using var ctx = InMemoryDbContextFactory.Create();
        using var uow = new TestUnitOfWork(ctx);
        var repo = uow.Repository<TestEntity>();
        repo.Insert(new TestEntity { Id = 1, Name = "Tracked" });

        // Act
        uow.ClearChangeTracker();

        // Assert — change tracker is empty
        Assert.Empty(ctx.ChangeTracker.Entries());
    }
}

/// <summary>Entity type intentionally not registered in <see cref="TestDbContext"/> — used for negative tests.</summary>
public class UnregisteredEntity
{
    public int Id { get; set; }
}
