using TBJ.Persistence.EfCore.Abstractions;
using TBJ.Persistence.EfCore.Implementation;
using TBJ.Persistence.EfCore.IntegrationTests.Helpers;

namespace TBJ.Persistence.EfCore.IntegrationTests.Repository;

/// <summary>
/// Integration tests for <see cref="IGenericRepository{TEntity}"/> using a real SQLite in-memory database.
/// Demonstrates the full persistence stack: entity configuration, repository operations and query composition.
/// </summary>
public class ProductRepositoryIntegrationTests : IDisposable
{
    private readonly SqliteDbContextFactory factory;

    /// <summary>Initializes a fresh SQLite database for each test.</summary>
    public ProductRepositoryIntegrationTests()
    {
        factory = new SqliteDbContextFactory();
    }

    /// <inheritdoc/>
    public void Dispose() => factory.Dispose();

    // -----------------------------------------------------------------------
    // Basic CRUD
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Insert_AndFindAsync_ReturnsCorrectEntity()
    {
        // Arrange
        using var ctx = factory.CreateContext();
        var repo = new GenericRepository<Product>(ctx);

        // Act
        repo.Insert(new Product { Id = 1, Name = "Widget", Price = 9.99m, Category = "Goods" });
        await ctx.SaveChangesAsync();

        // Assert
        var found = await repo.FindAsync(1);
        Assert.NotNull(found);
        Assert.Equal("Widget", found.Name);
        Assert.Equal(9.99m, found.Price);
    }

    [Fact]
    public async Task GetAsync_WithFilter_ReturnsOnlyActiveProducts()
    {
        // Arrange
        using var ctx = factory.CreateContext();
        var repo = new GenericRepository<Product>(ctx);
        repo.Insert([
            new Product { Id = 1, Name = "Active1", Category = "A", IsActive = true },
            new Product { Id = 2, Name = "Inactive", Category = "A", IsActive = false },
            new Product { Id = 3, Name = "Active2", Category = "A", IsActive = true }
        ]);
        await ctx.SaveChangesAsync();

        // Act
        var active = await repo.GetAsync(filter: p => p.IsActive);

        // Assert
        Assert.Equal(2, active.Count);
        Assert.All(active, p => Assert.True(p.IsActive));
    }

    [Fact]
    public async Task GetAsync_WithOrderBy_ReturnsSortedByName()
    {
        // Arrange — sortowanie po Name (string), bo SQLite nie obsługuje ORDER BY na decimal
        using var ctx = factory.CreateContext();
        var repo = new GenericRepository<Product>(ctx);
        repo.Insert([
            new Product { Id = 1, Name = "C_Expensive", Price = 100m, Category = "X" },
            new Product { Id = 2, Name = "A_Cheap", Price = 5m, Category = "X" },
            new Product { Id = 3, Name = "B_Medium", Price = 50m, Category = "X" }
        ]);
        await ctx.SaveChangesAsync();

        // Act
        var sorted = await repo.GetAsync(orderBy: q => q.OrderBy(p => p.Name));

        // Assert — oczekiwana kolejność alfabetyczna: A_, B_, C_
        Assert.Equal(["A_Cheap", "B_Medium", "C_Expensive"], sorted.Select(p => p.Name));
    }

    [Fact]
    public async Task GetAsync_WithPaging_ReturnsCorrectPage()
    {
        // Arrange
        using var ctx = factory.CreateContext();
        var repo = new GenericRepository<Product>(ctx);
        repo.Insert(Enumerable.Range(1, 10).Select(i =>
            new Product { Id = i, Name = $"P{i}", Price = i * 10m, Category = "Test" }));
        await ctx.SaveChangesAsync();

        // Act — page 2, page size 3 (skip 3, take 3)
        var page = await repo.GetAsync(skip: 3, take: 3);

        // Assert
        Assert.Equal(3, page.Count);
    }

    [Fact]
    public async Task Update_ChangesArePersisted()
    {
        // Arrange
        using var ctx = factory.CreateContext();
        var repo = new GenericRepository<Product>(ctx);
        repo.Insert(new Product { Id = 1, Name = "OldName", Price = 10m, Category = "C" });
        await ctx.SaveChangesAsync();

        // Act
        var product = await repo.FindAsync(1);
        product!.Name = "NewName";
        product.Price = 20m;
        repo.Update(product);
        await ctx.SaveChangesAsync();

        // Assert
        ctx.ChangeTracker.Clear();
        var updated = await repo.FindAsync(1);
        Assert.Equal("NewName", updated!.Name);
        Assert.Equal(20m, updated.Price);
    }

    [Fact]
    public async Task Delete_EntityIsRemoved()
    {
        // Arrange
        using var ctx = factory.CreateContext();
        var repo = new GenericRepository<Product>(ctx);
        var product = new Product { Id = 1, Name = "ToDelete", Price = 1m, Category = "D" };
        repo.Insert(product);
        await ctx.SaveChangesAsync();

        // Act
        repo.Delete(product);
        await ctx.SaveChangesAsync();

        // Assert
        Assert.Null(await repo.FindAsync(1));
    }

    // -----------------------------------------------------------------------
    // Aggregate queries
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CountAsync_ReturnsCorrectTotal()
    {
        // Arrange
        using var ctx = factory.CreateContext();
        var repo = new GenericRepository<Product>(ctx);
        repo.Insert(Enumerable.Range(1, 7).Select(i =>
            new Product { Id = i, Name = $"P{i}", Price = i, Category = "Count" }));
        await ctx.SaveChangesAsync();

        // Act
        int total = await repo.CountAsync();
        int filtered = await repo.CountAsync(p => p.Price > 3);

        // Assert
        Assert.Equal(7, total);
        Assert.Equal(4, filtered);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsCorrectResult()
    {
        // Arrange
        using var ctx = factory.CreateContext();
        var repo = new GenericRepository<Product>(ctx);
        repo.Insert(new Product { Id = 1, Name = "Exists", Price = 1m, Category = "E" });
        await ctx.SaveChangesAsync();

        // Assert
        Assert.True(await repo.ExistsAsync(p => p.Name == "Exists"));
        Assert.False(await repo.ExistsAsync(p => p.Name == "Ghost"));
    }

    // -----------------------------------------------------------------------
    // Bulk operations via ExecuteDelete / ExecuteUpdate
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteRangeAsync_RemovesMatchingRows()
    {
        // Arrange
        using var ctx = factory.CreateContext();
        var repo = new GenericRepository<Product>(ctx);
        repo.Insert([
            new Product { Id = 1, Name = "Keep", Price = 1m, Category = "K", IsActive = true },
            new Product { Id = 2, Name = "Remove1", Price = 2m, Category = "R", IsActive = false },
            new Product { Id = 3, Name = "Remove2", Price = 3m, Category = "R", IsActive = false }
        ]);
        await ctx.SaveChangesAsync();

        // Act
        int deleted = await repo.DeleteRangeAsync(p => !p.IsActive);

        // Assert
        Assert.Equal(2, deleted);
        Assert.Equal(1, await repo.CountAsync());
    }

    [Fact]
    public async Task UpdateRangeAsync_UpdatesMatchingRows()
    {
        // Arrange
        using var ctx = factory.CreateContext();
        var repo = new GenericRepository<Product>(ctx);
        repo.Insert([
            new Product { Id = 1, Name = "Old1", Price = 1m, Category = "Sale", IsActive = true },
            new Product { Id = 2, Name = "Old2", Price = 2m, Category = "Sale", IsActive = true },
            new Product { Id = 3, Name = "Other", Price = 3m, Category = "Full", IsActive = true }
        ]);
        await ctx.SaveChangesAsync();

        // Act — mark all Sale products as inactive
        int updated = await repo.UpdateRangeAsync(
            p => p.Category == "Sale",
            s => s.SetProperty(p => p.IsActive, false));

        // Assert
        Assert.Equal(2, updated);
        var saleProducts = await repo.GetAsync(p => p.Category == "Sale");
        Assert.All(saleProducts, p => Assert.False(p.IsActive));
    }
}
