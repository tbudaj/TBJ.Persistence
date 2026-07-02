using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TBJ.Persistence.EfCore.Implementation;

namespace TBJ.Persistence.EfCore.WebApiSample.Infrastructure;

/// <summary>
/// DbContext aplikacji WebAPI — demonstracja braku DbSet&lt;T&gt;.
/// Wszystkie encje (Product, Order, OrderItem) są rejestrowane automatycznie
/// przez klasy IEntityTypeConfiguration&lt;T&gt; odkrywane w tym assembly
/// przez BaseDbContext.OnModelCreating — ApplyConfigurationsFromAssembly.
/// </summary>
public class AppDbContext : BaseDbContext
{
    // Celowo brak właściwości DbSet<T> — to jest kluczowa cecha biblioteki.
    // Dostęp do encji odbywa się przez uow.Repository<T>().

    /// <summary>Inicjalizuje kontekst z podanymi opcjami EF Core.</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options, ILoggerFactory? loggerFactory = null)
        : base(options, loggerFactory) { }
}
