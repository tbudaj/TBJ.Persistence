using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace TBJ.Persistence.EfCore.Abstractions;

/// <summary>
/// Kontrakt generycznego repozytorium dla operacji CRUD na encjach EF Core.
/// Dostarcza asynchroniczne API, operacje masowe (bulk), ExecuteUpdate/Delete
/// oraz elastyczną kompozycję zapytań przez IQueryable.
/// </summary>
/// <typeparam name="TEntity">Typ encji zarządzanej przez repozytorium.</typeparam>
public interface IGenericRepository<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Buduje komponowalny <see cref="IQueryable{T}"/> bez materializacji danych.
    /// Domyślnie brak limitu — limity muszą być egzekwowane na warstwie HTTP/aplikacji.
    /// Przy stronicowaniu bez jawnego <paramref name="orderBy"/> stosuje automatyczne sortowanie po kluczu głównym.
    /// </summary>
    IQueryable<TEntity> AsQueryable(
        Expression<Func<TEntity, bool>>? filter = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
        int? skip = null,
        int? take = null,
        bool tracking = false);

    /// <summary>
    /// Materializuje zapytanie do <see cref="List{T}"/> asynchronicznie.
    /// </summary>
    Task<List<TEntity>> GetAsync(
        Expression<Func<TEntity, bool>>? filter = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
        int? skip = null,
        int? take = null,
        bool tracking = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Wyszukuje encję po kluczu głównym. Obsługuje klucze złożone.
    /// </summary>
    Task<TEntity?> FindAsync(params object[] ids);

    /// <summary>
    /// Sprawdza, czy istnieje encja spełniająca predykat — bez materializacji.
    /// </summary>
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zlicza encje spełniające predykat (AsNoTracking dla wydajności).
    /// </summary>
    Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Zwraca pierwszą encję spełniającą predykat lub <c>null</c>.
    /// </summary>
    Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
        bool tracking = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Oznacza pojedynczą encję do wstawienia. Wymaga wywołania <c>SaveChangesAsync</c>.
    /// </summary>
    void Insert(TEntity entity);

    /// <summary>
    /// Oznacza kolekcję encji do wstawienia. Wymaga wywołania <c>SaveChangesAsync</c>.
    /// </summary>
    void Insert(IEnumerable<TEntity> entities);

    /// <summary>
    /// Wykonuje masowe wstawianie przez EFCore.BulkExtensions — natychmiastowy zapis do bazy,
    /// z pominięciem change trackera. Efektywne dla dużych zbiorów danych.
    /// </summary>
    Task InsertRangeAsync(IEnumerable<TEntity> entities, BulkConfig? bulkConfig = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wstawia lub aktualizuje encję na podstawie klucza głównego.
    /// Wykonuje FindAsync przed operacją — dla dużych zbiorów rozważ bulk extensions.
    /// </summary>
    Task InsertOrUpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wstawia lub aktualizuje kolekcję encji (N+1 — FindAsync per encja).
    /// Dla dużych zbiorów rozważ bulk extensions.
    /// </summary>
    Task InsertOrUpdateAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wstawia wyłącznie nowe encje — pomija istniejące po kluczu głównym (N+1).
    /// Dla dużych zbiorów rozważ bulk extensions.
    /// </summary>
    Task InsertNewAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <summary>
    /// Oznacza pojedynczą encję do aktualizacji. Wymaga wywołania <c>SaveChangesAsync</c>.
    /// </summary>
    void Update(TEntity entity);

    /// <summary>
    /// Oznacza kolekcję encji do aktualizacji. Wymaga wywołania <c>SaveChangesAsync</c>.
    /// </summary>
    void Update(IEnumerable<TEntity> entities);

    /// <summary>
    /// Wykonuje masową aktualizację bezpośrednio w bazie danych przez ExecuteUpdate — bez trackingu, natychmiastowy zapis.
    /// </summary>
    /// <returns>Liczba zaktualizowanych wierszy.</returns>
#if NET10_0_OR_GREATER
    Task<int> UpdateRangeAsync(
        Expression<Func<TEntity, bool>> whereExpression,
        Action<UpdateSettersBuilder<TEntity>> setPropertyCalls,
        CancellationToken cancellationToken = default);
#else
    Task<int> UpdateRangeAsync(
        Expression<Func<TEntity, bool>> whereExpression,
        Expression<Func<SetPropertyCalls<TEntity>, SetPropertyCalls<TEntity>>> setPropertyCalls,
        CancellationToken cancellationToken = default);
#endif

    /// <summary>
    /// Oznacza pojedynczą encję do usunięcia. Wymaga wywołania <c>SaveChangesAsync</c>.
    /// </summary>
    void Delete(TEntity entity);

    /// <summary>
    /// Oznacza kolekcję encji do usunięcia. Wymaga wywołania <c>SaveChangesAsync</c>.
    /// </summary>
    void Delete(IEnumerable<TEntity> entities);

    /// <summary>
    /// Wykonuje masowe usuwanie bezpośrednio w bazie danych przez ExecuteDelete — bez trackingu, natychmiastowy zapis.
    /// </summary>
    /// <returns>Liczba usuniętych wierszy.</returns>
    Task<int> DeleteRangeAsync(Expression<Func<TEntity, bool>> whereExpression, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dołącza encję do kontekstu EF Core (włącza change tracking).
    /// </summary>
    void Attach(TEntity entity);

    /// <summary>
    /// Odłącza encję od kontekstu EF Core (wyłącza change tracking).
    /// </summary>
    void Detach(TEntity entity);
}
