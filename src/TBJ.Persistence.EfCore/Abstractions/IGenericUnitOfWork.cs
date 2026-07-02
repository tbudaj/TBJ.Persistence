namespace TBJ.Persistence.EfCore.Abstractions;

/// <summary>
/// Kontrakt jednostki pracy (Unit of Work): zarządza repozytoriami, transakcjami
/// i cyklem życia DbContext. Gwarantuje atomowość operacji i dostarcza odpornych
/// transakcji przez ExecutionStrategy.
/// </summary>
public interface IGenericUnitOfWork : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Dostęp do bazowego <see cref="Implementation.BaseDbContext"/> dla operacji masowych i zaawansowanych scenariuszy.
    /// </summary>
    Implementation.BaseDbContext Context { get; }

    /// <summary>
    /// Pobiera lub tworzy repozytorium dla podanego typu encji (cache per instancja UoW).
    /// Zgłasza <see cref="InvalidOperationException"/> gdy typ encji nie jest zarejestrowany w modelu.
    /// </summary>
    IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class;

    /// <summary>
    /// Rozpoczyna nową transakcję bazodanową. Idempotentna — kolejne wywołania są ignorowane z ostrzeżeniem.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Zatwierdza aktywną transakcję. Brak operacji gdy transakcja nie jest aktywna.
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Wycofuje aktywną transakcję. Brak operacji gdy transakcja nie jest aktywna.
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Wykonuje surowe polecenie SQL (<c>ExecuteSqlRawAsync</c>).
    /// Zawsze używaj zapytań parametryzowanych — nigdy nie wstrzykuj surowych danych użytkownika.
    /// </summary>
    /// <returns>Liczba dotkniętych wierszy.</returns>
    Task<int> ExecuteSqlCommandAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tworzy komponowalny <see cref="IQueryable{T}"/> z surowego SQL (<c>FromSqlRaw</c>).
    /// Dalsza kompozycja LINQ jest tłumaczona na SQL.
    /// </summary>
    IQueryable<TResult> FromSql<TResult>(string query) where TResult : class;

    /// <summary>
    /// Przeładowuje encję z bazy danych, nadpisując lokalne zmiany.
    /// </summary>
    Task ReloadAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : class;

    /// <summary>
    /// Wykonuje akcję wewnątrz odpornej transakcji (ExecutionStrategy z automatycznym ponawianiem).
    /// Automatycznie rozpoczyna transakcję, wywołuje SaveChanges i zatwierdza lub wycofuje.
    /// Akcja musi być idempotentna — może być ponawiana wielokrotnie.
    /// </summary>
    Task ExecuteResilientTransactionAsync(Func<IGenericUnitOfWork, CancellationToken, Task> action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wariant generyczny <see cref="ExecuteResilientTransactionAsync"/> zwracający wynik.
    /// Akcja musi być idempotentna — może być ponawiana wielokrotnie.
    /// </summary>
    Task<T> ExecuteResilientTransactionAsync<T>(Func<IGenericUnitOfWork, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Czyści change tracker — odłącza wszystkie śledzone encje.
    /// Wymagane przed ponowieniem odpornej transakcji, aby uniknąć duplikatów po wycofaniu.
    /// </summary>
    void ClearChangeTracker();

    /// <summary>
    /// Zapisuje wszystkie śledzone zmiany do bazy danych.
    /// Nie zatwierdza transakcji — wywołaj <see cref="CommitTransactionAsync"/> osobno.
    /// </summary>
    /// <returns>Liczba zapisanych encji.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
