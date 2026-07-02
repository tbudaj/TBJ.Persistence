using TBJ.Persistence.EfCore.IntegrationTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using TBJ.Persistence.EfCore.Abstractions;
using TBJ.Persistence.EfCore.Extensions;
using Microsoft.EntityFrameworkCore;

namespace TBJ.Persistence.EfCore.IntegrationTests.UnitOfWork;

/// <summary>
/// Integration tests demonstrating the full UnitOfWork pattern with a real SQLite database.
/// Covers DI registration, transaction management and repository access.
/// </summary>
public class SampleUnitOfWorkIntegrationTests : IDisposable
{
    private readonly SqliteDbContextFactory factory;

    /// <summary>Creates a fresh SQLite database for each test.</summary>
    public SampleUnitOfWorkIntegrationTests()
    {
        factory = new SqliteDbContextFactory();
    }

    /// <inheritdoc/>
    public void Dispose() => factory.Dispose();

    // -----------------------------------------------------------------------
    // Basic UoW operations
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UoW_InsertAndSave_EntityIsPersisted()
    {
        // Arrange
        using var uow = factory.CreateUnitOfWork();
        var repo = uow.Repository<Product>();

        // Act
        repo.Insert(new Product { Id = 1, Name = "Persisted", Price = 42m, Category = "Demo" });
        await uow.SaveChangesAsync();

        // Assert
        var found = await repo.FindAsync(1);
        Assert.NotNull(found);
        Assert.Equal("Persisted", found.Name);
    }

    [Fact]
    public async Task UoW_MultipleRepositories_ShareSameContext()
    {
        // Arrange
        using var uow = factory.CreateUnitOfWork();

        // Act — accessing the same repo type twice
        var repo1 = uow.Repository<Product>();
        var repo2 = uow.Repository<Product>();

        // Assert — same cached instance
        Assert.Same(repo1, repo2);
    }

    // -----------------------------------------------------------------------
    // Transaction management
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Transaction_CommitPersistsChanges()
    {
        // Arrange
        using var uow = factory.CreateUnitOfWork();
        var repo = uow.Repository<Product>();

        // Act
        await uow.BeginTransactionAsync();
        repo.Insert(new Product { Id = 10, Name = "InTx", Price = 1m, Category = "TX" });
        await uow.SaveChangesAsync();
        await uow.CommitTransactionAsync();

        // Assert
        var found = await repo.FindAsync(10);
        Assert.NotNull(found);
    }

    [Fact]
    public async Task Transaction_RollbackDoesNotPersistChanges()
    {
        // Arrange
        using var uow = factory.CreateUnitOfWork();
        var repo = uow.Repository<Product>();

        // Act
        await uow.BeginTransactionAsync();
        repo.Insert(new Product { Id = 20, Name = "Rollback", Price = 1m, Category = "TX" });
        await uow.SaveChangesAsync();
        await uow.RollbackTransactionAsync();

        // Assert — SQLite in-memory supports real transactions; rolled-back data must not be visible
        uow.ClearChangeTracker();
        var notFound = await repo.FindAsync(20);
        Assert.Null(notFound);
    }

    // -----------------------------------------------------------------------
    // DI registration — both AddPersistence overloads
    // -----------------------------------------------------------------------

    [Fact]
    public void AddPersistence_DelegateOverload_ResolvesUoW()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act — advanced overload with IServiceProvider delegate
        services.AddPersistence<SampleDbContext, SampleUnitOfWork>(
            (_, opt) => opt.UseSqlite("DataSource=:memory:"));

        var provider = services.BuildServiceProvider();

        // Assert
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IGenericUnitOfWork>());
    }

    [Fact]
    public void AddPersistence_ConfigurationOverload_ResolvesUoW()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Sample"] = "DataSource=:memory:"
            })
            .Build();

        // Act — simple overload reading connection string from IConfiguration
        services.AddPersistence<SampleDbContext, SampleUnitOfWork>(
            configuration,
            "Sample",
            (opt, cs) => opt.UseSqlite(cs));

        var provider = services.BuildServiceProvider();

        // Assert
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IGenericUnitOfWork>());
    }
}
