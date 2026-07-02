using Microsoft.Extensions.Logging;
using TBJ.Persistence.EfCore.Implementation;

namespace TBJ.Persistence.EfCore.WebApiSample.Infrastructure;

/// <summary>
/// Unit of Work dla AppDbContext.
/// Minimalny — wystarczy konstruktor przekazujący do klasy bazowej.
/// Wszystkie metody Repository, SaveChanges, transakcje dziedziczone z GenericUnitOfWork.
/// </summary>
public class AppUnitOfWork : GenericUnitOfWork<AppDbContext>
{
    /// <summary>Inicjalizuje UoW dla podanego kontekstu.</summary>
    public AppUnitOfWork(AppDbContext dbContext, ILoggerFactory? loggerFactory = null)
        : base(dbContext, loggerFactory) { }
}
