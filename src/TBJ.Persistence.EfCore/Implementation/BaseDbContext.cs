using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TBJ.Persistence.EfCore.Implementation;

/// <summary>
/// Abstrakcyjny bazowy DbContext z ustrukturyzowanym logowaniem i automatycznym wykrywaniem konfiguracji encji.
/// Dostawca bazy danych i connection string muszą być konfigurowane zewnętrznie (DI lub fabryka design-time).
/// Kategoria loggera pochodzi z konkretnego typu przez <c>GetType()</c> — poprawne kategorie per kontekst.
/// </summary>
public abstract class BaseDbContext : DbContext
{
    // Logger dla operacji cyklu życia DbContext
    private readonly ILogger logger;

    // Fabryka loggerów przekazywana do EF Core dla logowania zapytań i poleceń
    private readonly ILoggerFactory loggerFactory;

    /// <summary>
    /// Inicjalizuje nowy <see cref="BaseDbContext"/> ze standardowymi opcjami EF Core.
    /// </summary>
    /// <param name="options">Opcje EF Core konfigurowane przez DI lub fabrykę design-time.</param>
    /// <param name="loggerFactory">Opcjonalna fabryka loggerów. Gdy null, używany jest <see cref="NullLoggerFactory"/>.</param>
    protected BaseDbContext(DbContextOptions options, ILoggerFactory? loggerFactory = null)
        : base(options)
    {
        this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        logger = this.loggerFactory.CreateLogger(GetType());
        logger.LogDebug("DbContext {ContextType} zainicjalizowany z DbContextOptions.", GetType().Name);
    }

    /// <summary>
    /// Stosuje wszystkie implementacje <see cref="IEntityTypeConfiguration{TEntity}"/> znalezione w assembly
    /// konkretnej klasy kontekstu. Nadpisz i wywołaj <c>base.OnModelCreating(modelBuilder)</c>, aby rozszerzyć.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var assembly = GetType().Assembly;
        modelBuilder.ApplyConfigurationsFromAssembly(assembly);

        logger.LogDebug("OnModelCreating: zastosowano konfiguracje z assembly '{Assembly}'.", assembly.GetName().Name);
    }

    /// <summary>
    /// Konfiguruje opcje EF Core wspólne dla wszystkich dostawców (logowanie, dane wrażliwe).
    /// Dostawca i connection string muszą być ustawiane poza tą klasą bazową.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        logger.LogDebug("OnConfiguring: {ContextType}.", GetType().Name);

        // Wspólna konfiguracja EF Core utrzymywana w jednej klasie bazowej.
        // Lazy loading proxies celowo wyłączone — używaj jawnych .Include() w GenericRepository.
        optionsBuilder.UseLoggerFactory(loggerFactory);

#if DEBUG
        // Logowanie SQL na konsolę tylko w buildach DEBUG
        optionsBuilder
            .LogTo(Console.WriteLine)
            .EnableSensitiveDataLogging();
#endif

        base.OnConfiguring(optionsBuilder);
    }
}
