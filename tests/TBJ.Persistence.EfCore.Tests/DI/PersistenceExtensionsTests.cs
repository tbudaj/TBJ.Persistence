using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TBJ.Persistence.EfCore.Abstractions;
using TBJ.Persistence.EfCore.Extensions;
using TBJ.Persistence.EfCore.Tests.Helpers;

namespace TBJ.Persistence.EfCore.Tests.DI;

/// <summary>Testy dla metod rejestracji DI w <see cref="PersistenceServiceCollectionExtensions"/>.</summary>
public class PersistenceExtensionsTests
{
    [Fact]
    public void AddUnitOfWork_RegistersUoWAndInterface()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestDbContext>(opt => opt.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        // Act
        services.AddUnitOfWork<TestUnitOfWork>();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<TestUnitOfWork>());
        Assert.NotNull(provider.GetService<IGenericUnitOfWork>());
    }

    [Fact]
    public void AddUnitOfWork_InterfaceAndConcreteAreSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestDbContext>(opt => opt.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddUnitOfWork<TestUnitOfWork>();
        var provider = services.BuildServiceProvider();

        // Act
        using var scope = provider.CreateScope();
        var concrete = scope.ServiceProvider.GetRequiredService<TestUnitOfWork>();
        var iface = scope.ServiceProvider.GetRequiredService<IGenericUnitOfWork>();

        // Assert — obie rejestracje wskazują na tę samą instancję Scoped
        Assert.Same(concrete, iface);
    }

    [Fact]
    public void AddPersistence_WithDelegate_RegistersDbContextAndUoW()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddPersistence<TestDbContext, TestUnitOfWork>(
            (_, opt) => opt.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<TestDbContext>());
        Assert.NotNull(provider.GetService<IGenericUnitOfWork>());
    }

    [Fact]
    public void AddPersistence_WithConfiguration_RegistersDbContextAndUoW()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var dbName = Guid.NewGuid().ToString();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "DataSource=file::memory:?cache=shared;Mode=Memory"
            })
            .Build();

        // Act — prosta przeciążona wersja odczytująca connection string z IConfiguration
        services.AddPersistence<TestDbContext, TestUnitOfWork>(
            configuration,
            "Default",
            (opt, _) => opt.UseInMemoryDatabase(dbName));

        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<TestDbContext>());
        Assert.NotNull(provider.GetService<IGenericUnitOfWork>());
    }

    [Fact]
    public void AddPersistence_MissingConnectionString_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        // Act & Assert — AddPersistence powinno zgłosić wyjątek przy braku connection stringa
        Assert.Throws<InvalidOperationException>(() =>
            services.AddPersistence<TestDbContext, TestUnitOfWork>(
                configuration,
                "NonExistent",
                (opt, cs) => opt.UseInMemoryDatabase(cs)));
    }
}
