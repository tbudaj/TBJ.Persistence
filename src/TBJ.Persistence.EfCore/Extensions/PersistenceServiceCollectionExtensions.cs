using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TBJ.Persistence.EfCore.Abstractions;
using TBJ.Persistence.EfCore.Implementation;

namespace TBJ.Persistence.EfCore.Extensions;

/// <summary>
/// Metody rozszerzające <see cref="IServiceCollection"/> do rejestracji warstwy persystencji.
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Rejestruje <typeparamref name="TUnitOfWork"/> jako Scoped i wiąże go z <see cref="IGenericUnitOfWork"/>.
    /// Używaj gdy DbContext jest już zarejestrowany osobno.
    /// </summary>
    /// <typeparam name="TUnitOfWork">Konkretna implementacja UoW dziedzicząca po <see cref="GenericUnitOfWork{TDbContext}"/>.</typeparam>
    public static IServiceCollection AddUnitOfWork<TUnitOfWork>(this IServiceCollection services)
        where TUnitOfWork : class, IGenericUnitOfWork
    {
        services.AddScoped<TUnitOfWork>();
        services.AddScoped<IGenericUnitOfWork>(sp => sp.GetRequiredService<TUnitOfWork>());
        return services;
    }

    /// <summary>
    /// Rejestruje <typeparamref name="TDbContext"/> i <typeparamref name="TUnitOfWork"/> przez delegat konfiguracyjny.
    /// <para>
    /// <b>Scenariusz zaawansowany / multi-tenancy:</b> delegat <paramref name="configureDbContext"/> otrzymuje
    /// bieżący <see cref="IServiceProvider"/>, co umożliwia resolwowanie <see cref="IConnectionStringProvider"/>
    /// do dostarczania connection stringa per-request.
    /// </para>
    /// </summary>
    /// <typeparam name="TDbContext">Typ kontekstu EF Core dziedziczący po <see cref="BaseDbContext"/>.</typeparam>
    /// <typeparam name="TUnitOfWork">Typ UoW obsługujący dany kontekst.</typeparam>
    /// <param name="services">Kolekcja usług DI, do której dodawane są rejestracje.</param>
    /// <param name="configureDbContext">Delegat konfigurujący dostawcę i connection string.</param>
    /// <param name="contextLifetime">Czas życia usługi DbContext (domyślnie Scoped).</param>
    /// <param name="optionsLifetime">Czas życia usługi DbContextOptions (domyślnie Scoped).</param>
    public static IServiceCollection AddPersistence<TDbContext, TUnitOfWork>(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> configureDbContext,
        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
        where TDbContext : BaseDbContext
        where TUnitOfWork : class, IGenericUnitOfWork
    {
        ArgumentNullException.ThrowIfNull(configureDbContext);

        services.AddDbContext<TDbContext>(configureDbContext, contextLifetime, optionsLifetime);
        services.AddUnitOfWork<TUnitOfWork>();

        return services;
    }

    /// <summary>
    /// Rejestruje <typeparamref name="TDbContext"/> i <typeparamref name="TUnitOfWork"/> używając prostego
    /// connection stringa z <see cref="IConfiguration"/>.
    /// <para>
    /// <b>Scenariusz prosty:</b> connection string odczytywany jest z <paramref name="configuration"/>
    /// po nazwie <paramref name="connectionStringName"/> i przekazywany do delegatu <paramref name="configureProvider"/>.
    /// </para>
    /// </summary>
    /// <typeparam name="TDbContext">Typ kontekstu EF Core dziedziczący po <see cref="BaseDbContext"/>.</typeparam>
    /// <typeparam name="TUnitOfWork">Typ UoW obsługujący dany kontekst.</typeparam>
    /// <param name="services">Kolekcja usług DI, do której dodawane są rejestracje.</param>
    /// <param name="configuration">Konfiguracja aplikacji (używana do odczytu connection stringa).</param>
    /// <param name="connectionStringName">Nazwa connection stringa w <c>appsettings.json</c>.</param>
    /// <param name="configureProvider">Delegat stosujący dostawcę (np. <c>opt.UseSqlServer(cs)</c>).</param>
    /// <param name="contextLifetime">Czas życia usługi DbContext (domyślnie Scoped).</param>
    /// <param name="optionsLifetime">Czas życia usługi DbContextOptions (domyślnie Scoped).</param>
    /// <exception cref="InvalidOperationException">Gdy nazwany connection string nie istnieje w konfiguracji.</exception>
    public static IServiceCollection AddPersistence<TDbContext, TUnitOfWork>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName,
        Action<DbContextOptionsBuilder, string> configureProvider,
        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
        where TDbContext : BaseDbContext
        where TUnitOfWork : class, IGenericUnitOfWork
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionStringName);
        ArgumentNullException.ThrowIfNull(configureProvider);

        var connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{connectionStringName}' nie znaleziony w konfiguracji.");

        return services.AddPersistence<TDbContext, TUnitOfWork>(
            (_, opt) => configureProvider(opt, connectionString),
            contextLifetime,
            optionsLifetime);
    }
}
