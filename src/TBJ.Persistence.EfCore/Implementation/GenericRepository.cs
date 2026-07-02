using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using TBJ.Persistence.EfCore.Abstractions;

namespace TBJ.Persistence.EfCore.Implementation;

/// <summary>
/// Generyczne repozytorium dla encji EF Core.
/// Obsługuje CRUD, operacje masowe (EFCore.BulkExtensions), ExecuteUpdate/Delete
/// oraz komponowalny IQueryable.
/// Wątkobezpieczna pamięć podręczna wyrażeń OrderBy po kluczu głównym per typ encji.
/// </summary>
public sealed class GenericRepository<TEntity> : IGenericRepository<TEntity>
    where TEntity : class
{
    /// <summary>Wątkobezpieczna pamięć podręczna wyrażeń OrderBy po PK — po jednym na typ encji.</summary>
    private static readonly ConcurrentDictionary<Type, LambdaExpression?> PrimaryKeyOrderByCache = new();

    private readonly DbContext dbContext;
    private readonly DbSet<TEntity> set;

    // Logger dla operacji repozytorium
    private readonly ILogger<GenericRepository<TEntity>> logger;

    /// <summary>Inicjalizuje repozytorium dla podanego DbContext.</summary>
    /// <param name="dbContext">Kontekst EF Core, na którym operuje repozytorium.</param>
    /// <param name="logger">Opcjonalny logger. Gdy null, używany jest <see cref="NullLogger{T}"/>.</param>
    public GenericRepository(DbContext dbContext, ILogger<GenericRepository<TEntity>>? logger = null)
    {
        this.dbContext = dbContext;
        this.logger = logger ?? NullLogger<GenericRepository<TEntity>>.Instance;
        set = dbContext.Set<TEntity>();
        this.logger.LogDebug("Zainicjalizowano GenericRepository<{Entity}>.", typeof(TEntity).Name);
    }

    /// <summary>
    /// Buduje komponowalny <see cref="IQueryable{TEntity}"/> z opcjonalnym filtrem, sortowaniem,
    /// dołączeniami, stronicowaniem i trybem śledzenia.
    /// Automatyczne sortowanie po PK stosowane tylko przy stronicowaniu bez jawnego <paramref name="orderBy"/>.
    /// Domyślnie brak limitu — limity muszą być egzekwowane na warstwie HTTP/aplikacji.
    /// </summary>
    public IQueryable<TEntity> AsQueryable(
        Expression<Func<TEntity, bool>>? filter = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
        int? skip = null,
        int? take = null,
        bool tracking = false)
    {
        logger.LogDebug(
            "AsQueryable<{Entity}>: filtr={HasFilter}, include={HasInclude}, skip={Skip}, take={Take}, tracking={Tracking}.",
            typeof(TEntity).Name, filter is not null, include is not null, skip, take, tracking);

        IQueryable<TEntity> query = set;

        // Filtr stosowany jako pierwszy, aby ograniczyć zbiór danych dla kolejnych operacji
        if (filter is not null)
            query = query.Where(filter);

        // Dołączanie powiązanych encji (eager loading)
        if (include is not null)
            query = include(query);

        // AsNoTracking dla zapytań tylko do odczytu (domyślnie) — eliminuje narzut change trackera
        if (!tracking)
            query = query.AsNoTracking();

        // Stronicowanie wymaga deterministycznego sortowania — przy braku jawnego orderBy sortuj po PK
        var usesPaging = (skip.HasValue && skip.Value > 0) || take.HasValue;
        if (orderBy is not null)
        {
            query = orderBy(query);
            logger.LogDebug("AsQueryable<{Entity}>: zastosowano jawne sortowanie.", typeof(TEntity).Name);
        }
        else if (usesPaging)
        {
            query = ApplyDefaultOrderingIfPossible(query);
        }

        if (skip.HasValue && skip.Value > 0)
            query = query.Skip(skip.Value);

        // Brak domyślnego limitu — limity psują kompozycję LINQ (joiny, podzapytania)
        if (take.HasValue && take.Value > 0)
            query = query.Take(take.Value);

        return query;
    }

    /// <summary>Wyszukuje encję po kluczu głównym. Obsługuje klucze złożone.</summary>
    public Task<TEntity?> FindAsync(params object[] ids)
    {
        logger.LogDebug("FindAsync<{Entity}>: klucze = [{Keys}].", typeof(TEntity).Name, string.Join(", ", ids));
        return set.FindAsync(ids).AsTask();
    }

    /// <summary>Materializuje zapytanie do <see cref="List{TEntity}"/> asynchronicznie.</summary>
    public async Task<List<TEntity>> GetAsync(
        Expression<Func<TEntity, bool>>? filter = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
        int? skip = null,
        int? take = null,
        bool tracking = false,
        CancellationToken cancellationToken = default)
    {
        var result = await AsQueryable(filter, orderBy, include, skip, take, tracking).ToListAsync(cancellationToken);
        logger.LogDebug("GetAsync<{Entity}>: zwrócono {Count} rekordów.", typeof(TEntity).Name, result.Count);
        return result;
    }

    /// <summary>Sprawdza, czy istnieje encja spełniająca predykat (bez materializacji).</summary>
    public async Task<bool> ExistsAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var query = set.AsNoTracking();
        var result = predicate is null
            ? await query.AnyAsync(cancellationToken)
            : await query.AnyAsync(predicate, cancellationToken);
        logger.LogDebug("ExistsAsync<{Entity}>: {Result}.", typeof(TEntity).Name, result);
        return result;
    }

    /// <summary>Zlicza encje spełniające predykat (AsNoTracking dla wydajności).</summary>
    public async Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var query = set.AsNoTracking();
        var result = predicate is null
            ? await query.CountAsync(cancellationToken)
            : await query.CountAsync(predicate, cancellationToken);
        logger.LogDebug("CountAsync<{Entity}>: {Count}.", typeof(TEntity).Name, result);
        return result;
    }

    /// <summary>Zwraca pierwszą encję spełniającą predykat lub null.</summary>
    public async Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null,
        bool tracking = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        IQueryable<TEntity> query = set.Where(predicate);

        if (include is not null)
            query = include(query);

        if (orderBy is not null)
            query = orderBy(query);

        if (!tracking)
            query = query.AsNoTracking();

        var result = await query.FirstOrDefaultAsync(cancellationToken);
        logger.LogDebug("FirstOrDefaultAsync<{Entity}>: {Found}.", typeof(TEntity).Name, result is not null ? "znaleziono" : "nie znaleziono");
        return result;
    }

    /// <summary>Oznacza pojedynczą encję do wstawienia. Wymaga SaveChanges.</summary>
    public void Insert(TEntity entity)
    {
        logger.LogDebug("Insert<{Entity}>: 1 encja oznaczona do wstawienia.", typeof(TEntity).Name);
        set.Add(entity);
    }

    /// <summary>Oznacza kolekcję encji do wstawienia. Wymaga SaveChanges.</summary>
    public void Insert(IEnumerable<TEntity> entities)
    {
        var list = entities as ICollection<TEntity> ?? entities.ToList();
        logger.LogDebug("Insert<{Entity}>: {Count} encji oznaczonych do wstawienia.", typeof(TEntity).Name, list.Count);
        set.AddRange(list);
    }

    /// <summary>
    /// Masowe wstawianie przez EFCore.BulkExtensions — natychmiastowy zapis do bazy, z pominięciem change trackera.
    /// Efektywne dla dużych zbiorów danych.
    /// </summary>
    public async Task InsertRangeAsync(IEnumerable<TEntity> entities, BulkConfig? bulkConfig = null, CancellationToken cancellationToken = default)
    {
        var list = entities as IList<TEntity> ?? entities.ToList();
        logger.LogDebug("InsertRangeAsync<{Entity}>: masowe wstawianie {Count} rekordów.", typeof(TEntity).Name, list.Count);
        await dbContext.BulkInsertAsync(list, bulkConfig, cancellationToken: cancellationToken);
        logger.LogInformation("InsertRangeAsync<{Entity}>: masowe wstawianie zakończone, {Count} rekordów.", typeof(TEntity).Name, list.Count);
    }

    /// <summary>
    /// Wstawia lub aktualizuje encję na podstawie klucza głównego.
    /// Wykonuje FindAsync przed operacją — dla dużych zbiorów rozważ BulkExtensions.
    /// </summary>
    public async Task InsertOrUpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var key = GetPrimaryKeyProperties();
        var values = GetKeyValues(entity, key);
        var existing = await set.FindAsync(values, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            logger.LogDebug("InsertOrUpdateAsync<{Entity}>: INSERT — brak istniejącego rekordu.", typeof(TEntity).Name);
            set.Add(entity);
        }
        else
        {
            logger.LogDebug("InsertOrUpdateAsync<{Entity}>: UPDATE — nadpisywanie istniejących wartości.", typeof(TEntity).Name);
            dbContext.Entry(existing).CurrentValues.SetValues(entity);
        }
    }

    /// <summary>
    /// Wstawia lub aktualizuje kolekcję encji (N+1 — FindAsync per encja).
    /// Dla dużych zbiorów rozważ BulkExtensions.
    /// </summary>
    public async Task InsertOrUpdateAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var list = entities as ICollection<TEntity> ?? entities.ToList();
        logger.LogDebug("InsertOrUpdateAsync<{Entity}>: przetwarzanie {Count} encji (N+1).", typeof(TEntity).Name, list.Count);
        foreach (var entity in list)
            await InsertOrUpdateAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Wstawia wyłącznie nowe encje — pomija istniejące po kluczu głównym (N+1).
    /// Dla dużych zbiorów rozważ BulkExtensions.
    /// </summary>
    public async Task InsertNewAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var list = entities as ICollection<TEntity> ?? entities.ToList();
        var key = GetPrimaryKeyProperties();
        int inserted = 0, skipped = 0;
        foreach (var entity in list)
        {
            var values = GetKeyValues(entity, key);
            var existing = await set.FindAsync(values, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                set.Add(entity);
                inserted++;
            }
            else
            {
                skipped++;
            }
        }
        logger.LogDebug("InsertNewAsync<{Entity}>: wstawiono {Inserted}, pominięto {Skipped} (już istnieją).",
            typeof(TEntity).Name, inserted, skipped);
    }

    /// <summary>Oznacza pojedynczą encję do aktualizacji. Wymaga SaveChanges.</summary>
    public void Update(TEntity entity)
    {
        logger.LogDebug("Update<{Entity}>: 1 encja oznaczona do aktualizacji.", typeof(TEntity).Name);
        set.Update(entity);
    }

    /// <summary>Oznacza kolekcję encji do aktualizacji. Wymaga SaveChanges.</summary>
    public void Update(IEnumerable<TEntity> entities)
    {
        var list = entities as ICollection<TEntity> ?? entities.ToList();
        logger.LogDebug("Update<{Entity}>: {Count} encji oznaczonych do aktualizacji.", typeof(TEntity).Name, list.Count);
        set.UpdateRange(list);
    }

    /// <summary>
    /// Masowa aktualizacja bezpośrednio w bazie danych przez ExecuteUpdate — bez trackingu, natychmiastowy zapis.
    /// Sygnatura zależy od wersji EF Core: EF Core 10+ używa Action/UpdateSettersBuilder,
    /// wcześniejsze wersje — Expression/SetPropertyCalls.
    /// </summary>
#if NET10_0_OR_GREATER
    public async Task<int> UpdateRangeAsync(
        Expression<Func<TEntity, bool>> whereExpression,
        Action<UpdateSettersBuilder<TEntity>> setPropertyCalls,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(whereExpression);
        ArgumentNullException.ThrowIfNull(setPropertyCalls);
        logger.LogDebug("UpdateRangeAsync<{Entity}>: wykonywanie masowego ExecuteUpdate (EF Core 10+).", typeof(TEntity).Name);
        var affected = await set.Where(whereExpression).ExecuteUpdateAsync(setPropertyCalls, cancellationToken);
        logger.LogInformation("UpdateRangeAsync<{Entity}>: zaktualizowano {Count} wierszy.", typeof(TEntity).Name, affected);
        return affected;
    }
#else
    public async Task<int> UpdateRangeAsync(
        Expression<Func<TEntity, bool>> whereExpression,
        Expression<Func<SetPropertyCalls<TEntity>, SetPropertyCalls<TEntity>>> setPropertyCalls,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(whereExpression);
        ArgumentNullException.ThrowIfNull(setPropertyCalls);
        logger.LogDebug("UpdateRangeAsync<{Entity}>: wykonywanie masowego ExecuteUpdate (EF Core 8/9).", typeof(TEntity).Name);
        var affected = await set.Where(whereExpression).ExecuteUpdateAsync(setPropertyCalls, cancellationToken);
        logger.LogInformation("UpdateRangeAsync<{Entity}>: zaktualizowano {Count} wierszy.", typeof(TEntity).Name, affected);
        return affected;
    }
#endif

    /// <summary>Oznacza pojedynczą encję do usunięcia. Wymaga SaveChanges.</summary>
    public void Delete(TEntity entity)
    {
        logger.LogDebug("Delete<{Entity}>: 1 encja oznaczona do usunięcia.", typeof(TEntity).Name);
        set.Remove(entity);
    }

    /// <summary>Oznacza kolekcję encji do usunięcia. Wymaga SaveChanges.</summary>
    public void Delete(IEnumerable<TEntity> entities)
    {
        var list = entities as ICollection<TEntity> ?? entities.ToList();
        logger.LogDebug("Delete<{Entity}>: {Count} encji oznaczonych do usunięcia.", typeof(TEntity).Name, list.Count);
        set.RemoveRange(list);
    }

    /// <summary>
    /// Masowe usuwanie bezpośrednio w bazie danych przez ExecuteDelete — bez trackingu, natychmiastowy zapis.
    /// </summary>
    public async Task<int> DeleteRangeAsync(Expression<Func<TEntity, bool>> whereExpression, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(whereExpression);
        logger.LogDebug("DeleteRangeAsync<{Entity}>: wykonywanie masowego ExecuteDelete.", typeof(TEntity).Name);
        var affected = await set.Where(whereExpression).ExecuteDeleteAsync(cancellationToken);
        logger.LogInformation("DeleteRangeAsync<{Entity}>: usunięto {Count} wierszy.", typeof(TEntity).Name, affected);
        return affected;
    }

    /// <summary>Dołącza encję do kontekstu EF Core (włącza change tracking).</summary>
    public void Attach(TEntity entity)
    {
        logger.LogDebug("Attach<{Entity}>: encja dołączona.", typeof(TEntity).Name);
        set.Attach(entity);
    }

    /// <summary>Odłącza encję od kontekstu EF Core (wyłącza change tracking).</summary>
    public void Detach(TEntity entity)
    {
        logger.LogDebug("Detach<{Entity}>: encja odłączona.", typeof(TEntity).Name);
        dbContext.Entry(entity).State = EntityState.Detached;
    }

    // -------------------------------------------------------------------------
    // Metody pomocnicze (private)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Pobiera listę właściwości klucza głównego encji z modelu EF.
    /// Zgłasza wyjątek gdy typ encji lub klucz nie jest zarejestrowany w modelu.
    /// </summary>
    private IReadOnlyList<IProperty> GetPrimaryKeyProperties()
    {
        var entityType = dbContext.Model.FindEntityType(typeof(TEntity));
        if (entityType is null)
            throw new InvalidOperationException($"Typ encji {typeof(TEntity).Name} nie jest zarejestrowany w modelu DbContext.");

        var key = entityType.FindPrimaryKey();
        if (key is null)
            throw new InvalidOperationException($"Typ encji {typeof(TEntity).Name} nie ma zdefiniowanego klucza głównego.");

        return key.Properties;
    }

    /// <summary>Zwraca tablicę wartości klucza głównego wyodrębnioną z podanej encji.</summary>
    private static object?[] GetKeyValues(TEntity entity, IReadOnlyList<IProperty> keyProperties)
    {
        var values = new object?[keyProperties.Count];
        for (int i = 0; i < keyProperties.Count; i++)
            values[i] = keyProperties[i].GetGetter().GetClrValue(entity);
        return values;
    }

    /// <summary>
    /// Stosuje domyślne sortowanie rosnące po kluczu głównym przy stronicowaniu bez jawnego orderBy.
    /// Cachuje wyrażenie lambda OrderBy per typ encji dla wydajności.
    /// Jeśli klucz główny nie może być rozwiązany, zwraca oryginalne zapytanie bez sortowania.
    /// </summary>
    private IQueryable<TEntity> ApplyDefaultOrderingIfPossible(IQueryable<TEntity> query)
    {
        var lambda = PrimaryKeyOrderByCache.GetOrAdd(typeof(TEntity), _ => BuildPrimaryKeyOrderByLambda());
        if (lambda is null)
        {
            logger.LogDebug("ApplyDefaultOrdering<{Entity}>: brak jednokolumnowego PK, sortowanie pominięte.", typeof(TEntity).Name);
            return query;
        }

        // Użycie refleksji do wywołania Queryable.OrderBy z poprawnym TKey
        var methodInfo = typeof(Queryable)
            .GetMethods()
            .First(m => m.Name == nameof(Queryable.OrderBy) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TEntity), lambda.ReturnType);

        logger.LogDebug("ApplyDefaultOrdering<{Entity}>: sortowanie po kluczu głównym.", typeof(TEntity).Name);
        return (IQueryable<TEntity>)methodInfo.Invoke(null, [query, lambda])!;
    }

    /// <summary>
    /// Buduje <c>Expression&lt;Func&lt;TEntity, TKey&gt;&gt;</c> dla pierwszej właściwości klucza głównego.
    /// Zwraca null gdy encja nie ma jednokolumnowego klucza lub klucz nie może być rozwiązany.
    /// </summary>
    private LambdaExpression? BuildPrimaryKeyOrderByLambda()
    {
        var entityType = dbContext.Model.FindEntityType(typeof(TEntity));
        var pk = entityType?.FindPrimaryKey();
        if (pk is null || pk.Properties.Count != 1)
            return null;

        var property = pk.Properties[0];
        var param = Expression.Parameter(typeof(TEntity), "e");
        var body = Expression.Property(param, property.Name);
        return Expression.Lambda(body, param);
    }
}
