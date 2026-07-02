using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using TBJ.Persistence.EfCore.Abstractions;

namespace TBJ.Persistence.EfCore.Implementation;

/// <summary>
/// Implementacja wzorca Unit of Work dla <see cref="BaseDbContext"/>.
/// DbContext dostarczany jest z DI lub tworzony zewnętrznie — UoW nie zna źródła connection stringa.
/// Zarządza per-instancyjną pamięcią podręczną repozytoriów, transakcjami, surowym SQL
/// i odpornymi transakcjami (ExecutionStrategy).
/// Implementuje zarówno <see cref="IDisposable"/>, jak i <see cref="IAsyncDisposable"/> —
/// w kontekstach asynchronicznych zalecane jest DisposeAsync.
/// </summary>
public abstract class GenericUnitOfWork<TDbContext> : IGenericUnitOfWork
    where TDbContext : BaseDbContext
{
    private readonly TDbContext dbContext;

    // Logger dla operacji cyklu życia UoW
    private readonly ILogger logger;

    // Fabryka loggerów przekazywana do tworzonych instancji GenericRepository
    private readonly ILoggerFactory loggerFactory;

    // Cache repozytoriów per typ encji — wątkobezpieczny, per instancja UoW
    private readonly ConcurrentDictionary<Type, object> repositories = new();

    // Aktywna transakcja bazodanowa; null gdy brak aktywnej transakcji
    private IDbContextTransaction? transaction;

    // Flaga disposed — zapobiega dalszemu użyciu po Dispose/DisposeAsync
    private bool disposed;

    /// <summary>Inicjalizuje jednostkę pracy dla podanego <see cref="DbContext"/>.</summary>
    /// <param name="dbContext">Kontekst dostarczony zewnętrznie.</param>
    /// <param name="loggerFactory">Opcjonalna fabryka loggerów. Gdy null, używany jest <see cref="NullLoggerFactory"/>.</param>
    public GenericUnitOfWork(TDbContext dbContext, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        this.dbContext = dbContext;
        this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        logger = this.loggerFactory.CreateLogger(GetType());
        logger.LogDebug("{UoW} zainicjalizowano dla {DbContext}.", GetType().Name, dbContext.GetType().Name);
    }

    /// <summary>
    /// Dostęp do bazowego DbContext dla operacji masowych i zaawansowanych scenariuszy.
    /// </summary>
    public BaseDbContext Context => dbContext;

    /// <summary>
    /// Pobiera lub tworzy repozytorium dla podanego typu encji (cache per instancja UoW).
    /// Fail-fast gdy typ encji nie jest zarejestrowany w modelu DbContext.
    /// </summary>
    /// <exception cref="InvalidOperationException">Gdy typ encji nie jest zarejestrowany w modelu.</exception>
    /// <exception cref="ObjectDisposedException">Gdy UoW został już zwolniony.</exception>
    public IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class
    {
        ThrowIfDisposed();
        var entityType = typeof(TEntity);

        // Fail-fast: encja musi być zarejestrowana w modelu EF
        if (dbContext.Model.FindEntityType(entityType) is null)
        {
            logger.LogError("Repository<{Entity}>: typ nie znaleziony w modelu {DbContext}.", entityType.Name, dbContext.GetType().Name);
            throw new InvalidOperationException($"Typ encji {entityType.Name} nie znaleziony w modelu {dbContext.GetType().Name}.");
        }

        // GetOrAdd jest wątkobezpieczny — tworzy repozytorium tylko przy pierwszym dostępie
        var repo = repositories.GetOrAdd(
            entityType,
            _ => new GenericRepository<TEntity>(dbContext, loggerFactory.CreateLogger<GenericRepository<TEntity>>()));

        logger.LogDebug("Repository<{Entity}>: pobrano z cache.", entityType.Name);

        return (IGenericRepository<TEntity>)repo;
    }

    /// <summary>
    /// Rozpoczyna nową transakcję bazodanową. Idempotentna — kolejne wywołania są ignorowane z ostrzeżeniem.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Gdy UoW został już zwolniony.</exception>
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (transaction is not null)
        {
            logger.LogWarning("BeginTransactionAsync: transakcja jest już aktywna — pominięto.");
            return;
        }

        transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        logger.LogDebug("BeginTransactionAsync: transakcja rozpoczęta.");
    }

    /// <summary>
    /// Zatwierdza aktywną transakcję i zwalnia jej zasoby. Brak operacji gdy transakcja nie jest aktywna.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Gdy UoW został już zwolniony.</exception>
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (transaction is null)
        {
            logger.LogDebug("CommitTransactionAsync: brak aktywnej transakcji — pominięto.");
            return;
        }

        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();

        transaction = null;
        logger.LogInformation("CommitTransactionAsync: transakcja zatwierdzona.");
    }

    /// <summary>
    /// Wycofuje aktywną transakcję i zwalnia jej zasoby. Brak operacji gdy transakcja nie jest aktywna.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Gdy UoW został już zwolniony.</exception>
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (transaction is null)
        {
            logger.LogDebug("RollbackTransactionAsync: brak aktywnej transakcji — pominięto.");
            return;
        }

        await transaction.RollbackAsync(cancellationToken);
        await transaction.DisposeAsync();

        transaction = null;
        logger.LogWarning("RollbackTransactionAsync: transakcja wycofana.");
    }

    /// <summary>
    /// Czyści change tracker — odłącza wszystkie śledzone encje.
    /// Wymagane przed ponowieniem odpornej transakcji, aby uniknąć duplikatów po wycofaniu.
    /// </summary>
    public void ClearChangeTracker()
    {
        ThrowIfDisposed();
        dbContext.ChangeTracker.Clear();
        logger.LogDebug("ClearChangeTracker: change tracker wyczyszczony.");
    }

    /// <summary>
    /// Zapisuje wszystkie śledzone zmiany do bazy danych.
    /// Nie zatwierdza transakcji — wywołaj CommitTransactionAsync osobno.
    /// </summary>
    /// <returns>Liczba zapisanych encji.</returns>
    /// <exception cref="ObjectDisposedException">Gdy UoW został już zwolniony.</exception>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        logger.LogDebug("SaveChangesAsync: zapisywanie śledzonych zmian.");

        var count = await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("SaveChangesAsync: zapisano {Count} encji.", count);

        return count;
    }

    /// <summary>
    /// Wykonuje surowe polecenie SQL (ExecuteSqlRawAsync).
    /// Zawsze używaj zapytań parametryzowanych — nigdy nie wstrzykuj surowych danych użytkownika.
    /// </summary>
    /// <returns>Liczba dotkniętych wierszy.</returns>
    /// <exception cref="ArgumentException">Gdy zapytanie jest null lub składa się wyłącznie z białych znaków.</exception>
    /// <exception cref="ObjectDisposedException">Gdy UoW został już zwolniony.</exception>
    public async Task<int> ExecuteSqlCommandAsync(string query, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        logger.LogDebug("ExecuteSqlCommandAsync: wykonywanie surowego polecenia SQL.");

        var affected = await dbContext.Database.ExecuteSqlRawAsync(query, cancellationToken);

        logger.LogInformation("ExecuteSqlCommandAsync: {Count} wierszy dotkniętych.", affected);

        return affected;
    }

    /// <summary>
    /// Tworzy komponowalny IQueryable z surowego SQL. Dalsza kompozycja LINQ tłumaczona jest na SQL.
    /// Zawsze używaj zapytań parametryzowanych — nigdy nie wstrzykuj surowych danych użytkownika.
    /// </summary>
    /// <exception cref="ArgumentException">Gdy zapytanie jest null lub składa się wyłącznie z białych znaków.</exception>
    /// <exception cref="ObjectDisposedException">Gdy UoW został już zwolniony.</exception>
    public IQueryable<TResult> FromSql<TResult>(string query) where TResult : class
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        logger.LogDebug("FromSql<{Result}>: budowanie IQueryable z surowego SQL.", typeof(TResult).Name);
        return dbContext.Set<TResult>().FromSqlRaw(query);
    }

    /// <summary>
    /// Przeładowuje encję z bazy danych, nadpisując lokalne zmiany.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Gdy UoW został już zwolniony.</exception>
    public async Task ReloadAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : class
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        logger.LogDebug("ReloadAsync<{Entity}>: przeładowywanie encji z bazy danych.", typeof(TEntity).Name);
        await dbContext.Entry(entity).ReloadAsync(cancellationToken);
    }

    /// <summary>
    /// Wykonuje akcję wewnątrz odpornej transakcji przez ExecutionStrategy z automatycznym ponawianiem.
    /// Automatycznie rozpoczyna transakcję, wywołuje SaveChanges i zatwierdza lub wycofuje przy błędzie.
    /// Akcja musi być idempotentna — change tracker jest czyszczony przed każdą próbą.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Gdy UoW został już zwolniony.</exception>
    public Task ExecuteResilientTransactionAsync(
        Func<IGenericUnitOfWork, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(action);

        logger.LogDebug("ExecuteResilientTransactionAsync: start odpornej transakcji.");

        return dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await BeginTransactionAsync(cancellationToken);
            try
            {
                ClearChangeTracker();
                await action(this, cancellationToken);
                await SaveChangesAsync(cancellationToken);
                await CommitTransactionAsync(cancellationToken);
                logger.LogInformation("ExecuteResilientTransactionAsync: transakcja zatwierdzona.");
            }
            catch
            {
                await RollbackTransactionAsync(cancellationToken);
                logger.LogWarning("ExecuteResilientTransactionAsync: transakcja wycofana z powodu błędu.");
                throw;
            }
        });
    }

    /// <summary>
    /// Wariant generyczny ExecuteResilientTransactionAsync zwracający wynik.
    /// Akcja musi być idempotentna — change tracker jest czyszczony przed każdą próbą.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Gdy UoW został już zwolniony.</exception>
    public Task<T> ExecuteResilientTransactionAsync<T>(
        Func<IGenericUnitOfWork, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(action);

        logger.LogDebug("ExecuteResilientTransactionAsync<{T}>: start odpornej transakcji.", typeof(T).Name);

        return dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await BeginTransactionAsync(cancellationToken);
            try
            {
                ClearChangeTracker();
                var result = await action(this, cancellationToken);
                await SaveChangesAsync(cancellationToken);
                await CommitTransactionAsync(cancellationToken);
                logger.LogInformation("ExecuteResilientTransactionAsync<{T}>: transakcja zatwierdzona.", typeof(T).Name);
                return result;
            }
            catch
            {
                await RollbackTransactionAsync(cancellationToken);
                logger.LogWarning("ExecuteResilientTransactionAsync<{T}>: transakcja wycofana z powodu błędu.", typeof(T).Name);
                throw;
            }
        });
    }

    /// <summary>Synchroniczne zwolnienie zasobów — zwalnia DbContext i aktywną transakcję.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Asynchroniczne zwolnienie zasobów — zalecane w kontekstach asynchronicznych.</summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;

        if (transaction is not null)
        {
            await transaction.DisposeAsync();
            transaction = null;
        }

        await dbContext.DisposeAsync();
        disposed = true;
        logger.LogDebug("{UoW} zwolniony (async).", GetType().Name);
        GC.SuppressFinalize(this);
    }

    /// <summary>Zgłasza <see cref="ObjectDisposedException"/> gdy UoW został zwolniony.</summary>
    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    /// <summary>Logika zwalniania zasobów dla ścieżki synchronicznej.</summary>
    private void Dispose(bool disposing)
    {
        if (disposed)
            return;

        if (disposing)
        {
            transaction?.Dispose();
            transaction = null;
            dbContext.Dispose();
        }

        disposed = true;
        logger.LogDebug("{UoW} zwolniony (sync).", GetType().Name);
    }
}
