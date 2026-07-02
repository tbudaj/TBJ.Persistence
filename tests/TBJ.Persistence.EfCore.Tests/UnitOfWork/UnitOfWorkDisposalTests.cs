using TBJ.Persistence.EfCore.Tests.Helpers;

namespace TBJ.Persistence.EfCore.Tests.UnitOfWork;

/// <summary>Tests verifying correct disposal behaviour of <see cref="TBJ.Persistence.EfCore.Implementation.GenericUnitOfWork{TDbContext}"/>.</summary>
public class UnitOfWorkDisposalTests
{
    [Fact]
    public async Task Dispose_SubsequentSaveChanges_ThrowsObjectDisposedException()
    {
        // Arrange
        var ctx = InMemoryDbContextFactory.Create();
        var uow = new TestUnitOfWork(ctx);

        // Act
        uow.Dispose();

        // Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => uow.SaveChangesAsync());
    }

    [Fact]
    public async Task DisposeAsync_SubsequentSaveChanges_ThrowsObjectDisposedException()
    {
        // Arrange
        var ctx = InMemoryDbContextFactory.Create();
        var uow = new TestUnitOfWork(ctx);

        // Act
        await uow.DisposeAsync();

        // Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => uow.SaveChangesAsync());
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var ctx = InMemoryDbContextFactory.Create();
        var uow = new TestUnitOfWork(ctx);

        // Act & Assert
        uow.Dispose();
        var ex = Record.Exception(() => uow.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var ctx = InMemoryDbContextFactory.Create();
        var uow = new TestUnitOfWork(ctx);

        // Act & Assert
        await uow.DisposeAsync();
        var ex = await Record.ExceptionAsync(() => uow.DisposeAsync().AsTask());
        Assert.Null(ex);
    }
}
