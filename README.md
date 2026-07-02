# TBJ.Persistence.EfCore

Generyczny wzorzec Repository i Unit of Work zbudowany na Entity Framework Core.  
Niezależny od dostawcy — działa z SQL Server, PostgreSQL (Npgsql), SQLite i każdym innym dostawcą EF Core.  
Obsługuje multi-tenancy (osobna baza danych per tenant) przez `IConnectionStringProvider`.

[![build](https://github.com/tbudaj/TBJ.Persistence/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/tbudaj/TBJ.Persistence/actions/workflows/build-and-test.yml)
[![NuGet](https://img.shields.io/nuget/v/TBJ.Persistence.EfCore)](https://www.nuget.org/packages/TBJ.Persistence.EfCore)

## Spis treści

1. [Instalacja](#instalacja)
2. [Co wyróżnia tę bibliotekę](#co-wyróżnia-tę-bibliotekę)
3. [Jak to działa — kluczowy mechanizm](#jak-to-działa--kluczowy-mechanizm)
4. [Szybki start](#szybki-start)
5. [Rejestracja DI](#rejestracja-di)
   - [Scenariusz prosty — connection string z IConfiguration](#scenariusz-prosty--connection-string-z-iconfiguration)
   - [Scenariusz zaawansowany — multi-tenancy z IConnectionStringProvider](#scenariusz-zaawansowany--multi-tenancy-z-iconnectionstringprovider)
6. [Multi-tenancy — osobna baza per tenant](#multi-tenancy--osobna-baza-per-tenant)
7. [IGenericRepository — API](#igenericrepository--api)
8. [IGenericUnitOfWork — API](#igenericunitofwork--api)
9. [Operacje masowe (bulk)](#operacje-masowe-bulk)
10. [Odporne transakcje](#odporne-transakcje)
11. [Testowanie](#testowanie)
12. [Czasy życia usług](#czasy-życia-usług)
13. [Przykładowe WebAPI](#przykładowe-webapi)
14. [Wersjonowanie i publikacja NuGet](#wersjonowanie-i-publikacja-nuget)

---

## Instalacja

```bash
dotnet add package TBJ.Persistence.EfCore
```

> Paczka celuje w `net8.0`, `net9.0` i `net10.0`.  
> **Nie ma zależności od konkretnego dostawcy EF Core** — dodaj paczkę dostawcy osobno.

```bash
# SQL Server
dotnet add package Microsoft.EntityFrameworkCore.SqlServer

# PostgreSQL
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL

# SQLite (najczęściej w testach i lokalnym dev)
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

---

## Co wyróżnia tę bibliotekę

### Brak DbSet\<T\> w DbContext

Klasyczne podejście EF Core wymaga deklarowania `DbSet<T>` dla każdej encji. Tu tego nie ma.  
Encje są rejestrowane w modelu **automatycznie** przez klasy konfiguracyjne `IEntityTypeConfiguration<T>`.

```csharp
// ❌ Tradycyjne podejście — boilerplate per encja
public class AppDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<Customer> Customers { get; set; }
    // każda nowa encja = zmiana kontekstu
}

// ✅ Podejście tej biblioteki — zero właściwości DbSet<>
public class AppDbContext : BaseDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    // Koniec. Nowe encje dodajesz tylko przez nową klasę IEntityTypeConfiguration<T>.
}
```

### Generyczne repozytoria — zero klas repository

Nie piszesz klas `OrderRepository`, `ProductRepository` itd. Repozytorium dla dowolnej encji pobierasz jedną linią:

```csharp
var repo = uow.Repository<Order>();     // gotowe
var repo = uow.Repository<Product>();   // gotowe
var repo = uow.Repository<Customer>();  // gotowe
// żadnego kodu między nową encją a pierwszym zapytaniem
```

### Multi-tenancy z osobną bazą per tenant

`IConnectionStringProvider<TDbContext>` jest resolwowany z DI per-request — każdy request może dostać inny connection string na podstawie JWT, nagłówka, cache tenantów lub dowolnej innej logiki.

### Operacje masowe bez change trackera

`InsertRangeAsync` (przez EFCore.BulkExtensions), `UpdateRangeAsync` i `DeleteRangeAsync` (przez natywne `ExecuteUpdate`/`ExecuteDelete` EF Core) — bez `SaveChanges`, bez narzutu trackera.

### Odporne transakcje z automatycznym retry

`ExecuteResilientTransactionAsync` opakowuje akcję w `ExecutionStrategy` EF Core — automatyczne ponowienie przy błędach przejściowych (np. SQL Server `EnableRetryOnFailure`).

---

## Jak to działa — kluczowy mechanizm

### Przepływ rejestracji encji

```
IEntityTypeConfiguration<Order>        ← jedyne co piszesz per encja
         │
         ▼
BaseDbContext.OnModelCreating()
→ ApplyConfigurationsFromAssembly()    ← auto-odkrywanie z assembly kontekstu
         │
         ▼
Model EF Core zna Order
         │
         ▼
uow.Repository<Order>()
→ dbContext.Set<Order>()               ← dynamiczny DbSet bez właściwości w klasie
→ GenericRepository<Order>             ← cachowany w ConcurrentDictionary per scope
```

### Co sprawdza Repository\<T\>()

Przed stworzeniem repozytorium wykonywane jest:

```csharp
if (dbContext.Model.FindEntityType(typeof(TEntity)) is null)
    throw new InvalidOperationException("Typ encji nie znaleziony w modelu.");
```

**Fail-fast** — jeśli zapomnisz napisać `OrderConfiguration`, dostaniesz czytelny wyjątek przy pierwszym użyciu `Repository<Order>()`, nie przy wysyłaniu zapytania do bazy.

---

## Szybki start

### Krok 1 — encja i konfiguracja (jedyne wymagane pliki)

```csharp
// Domain/Order.cs
public class Order
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

// Domain/OrderConfiguration.cs
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.CustomerName).IsRequired().HasMaxLength(200);
        builder.Property(o => o.Total).HasColumnType("decimal(18,2)");
        builder.Property(o => o.CreatedAt).IsRequired();
    }
}
```

### Krok 2 — DbContext bez DbSet\<\>

```csharp
// Infrastructure/AppDbContext.cs
public class AppDbContext : BaseDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    // Żadnych DbSet<T>. BaseDbContext.OnModelCreating odkrywa OrderConfiguration automatycznie.
}
```

### Krok 3 — UnitOfWork z jednym konstruktorem

```csharp
// Infrastructure/AppUnitOfWork.cs
public class AppUnitOfWork : GenericUnitOfWork<AppDbContext>
{
    public AppUnitOfWork(AppDbContext dbContext, ILoggerFactory? loggerFactory = null)
        : base(dbContext, loggerFactory) { }
}
```

### Krok 4 — rejestracja i użycie

```csharp
// Program.cs
builder.Services.AddPersistence<AppDbContext, AppUnitOfWork>(
    builder.Configuration, "Default",
    (opt, cs) => opt.UseSqlServer(cs));

// appsettings.json
{
  "ConnectionStrings": {
    "Default": "Server=.;Database=AppDb;Trusted_Connection=True;"
  }
}
```

```csharp
// OrderService.cs — korzystasz z IGenericUnitOfWork
public class OrderService(IGenericUnitOfWork uow)
{
    public async Task<List<Order>> GetActiveOrdersAsync()
        => await uow.Repository<Order>().GetAsync(filter: o => o.IsActive);

    public async Task<Order?> GetByIdAsync(int id)
        => await uow.Repository<Order>().FindAsync(id);

    public async Task CreateAsync(Order order)
    {
        uow.Repository<Order>().Insert(order);
        await uow.SaveChangesAsync();
    }

    public async Task DeactivateOldOrdersAsync(DateTime before)
        => await uow.Repository<Order>().UpdateRangeAsync(
            o => o.CreatedAt < before,
            s => s.SetProperty(o => o.IsActive, false));
}
```

---

## Rejestracja DI

### Scenariusz prosty — connection string z IConfiguration

```json
// appsettings.json
{
  "ConnectionStrings": {
    "Default": "Server=.;Database=AppDb;Trusted_Connection=True;"
  }
}
```

```csharp
builder.Services.AddPersistence<AppDbContext, AppUnitOfWork>(
    builder.Configuration,
    "Default",
    (opt, connectionString) => opt.UseSqlServer(connectionString));
```

Gdy DbContext jest już zarejestrowany przez inny mechanizm — rejestruj tylko UoW:

```csharp
services.AddUnitOfWork<AppUnitOfWork>();
```

### Scenariusz zaawansowany — multi-tenancy z IConnectionStringProvider

```csharp
// Zarejestruj dostawcę connection stringa per-request
builder.Services.AddScoped<IConnectionStringProvider<AppDbContext>, TenantConnectionStringProvider>();

// Delegate otrzymuje IServiceProvider — resolwuj connection string z własnego providera
builder.Services.AddPersistence<AppDbContext, AppUnitOfWork>((serviceProvider, opt) =>
{
    var provider = serviceProvider.GetRequiredService<IConnectionStringProvider<AppDbContext>>();
    opt.UseSqlServer(provider.GetConnectionString());
});
```

---

## Multi-tenancy — osobna baza per tenant

Wzorzec: każdy tenant ma własną bazę danych. Connection string jest resolwowany na podstawie identyfikatora tenanta z JWT lub nagłówka HTTP.

### 1. Implementacja IConnectionStringProvider

```csharp
// Infrastructure/TenantConnectionStringProvider.cs
public class TenantConnectionStringProvider : IConnectionStringProvider<AppDbContext>
{
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly ITenantStore tenantStore;

    public TenantConnectionStringProvider(
        IHttpContextAccessor httpContextAccessor,
        ITenantStore tenantStore)
    {
        this.httpContextAccessor = httpContextAccessor;
        this.tenantStore = tenantStore;
    }

    /// <summary>
    /// Resolwuje connection string dla bieżącego tenanta z JWT claim "tenant_id".
    /// Wywoływany raz per Scoped lifetime (jeden DbContext per HTTP request).
    /// </summary>
    public string GetConnectionString()
    {
        var tenantId = httpContextAccessor.HttpContext?.User.FindFirstValue("tenant_id")
            ?? throw new InvalidOperationException("Brak identyfikatora tenanta w JWT.");

        return tenantStore.GetConnectionString(tenantId)
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' nie znaleziony.");
    }
}
```

### 2. Rejestracja

```csharp
// Program.cs
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantStore, DatabaseTenantStore>();
builder.Services.AddScoped<IConnectionStringProvider<AppDbContext>, TenantConnectionStringProvider>();

builder.Services.AddPersistence<AppDbContext, AppUnitOfWork>((sp, opt) =>
{
    var provider = sp.GetRequiredService<IConnectionStringProvider<AppDbContext>>();
    opt.UseSqlServer(provider.GetConnectionString(), sqlOpt =>
        sqlOpt.EnableRetryOnFailure(maxRetryCount: 3));
});
```

### 3. Użycie — przezroczyste dla warstwy aplikacji

```csharp
// Serwis aplikacji nie wie nic o multi-tenancy — działa identycznie jak single-tenant
public class OrderService(IGenericUnitOfWork uow)
{
    // Każde wywołanie operuje na bazie konkretnego tenanta z bieżącego requestu
    public async Task<List<Order>> GetOrdersAsync()
        => await uow.Repository<Order>().GetAsync();
}
```

### Wariant — connection string z nagłówka HTTP

```csharp
public string GetConnectionString()
{
    var tenantHeader = httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].ToString();
    if (string.IsNullOrEmpty(tenantHeader))
        throw new InvalidOperationException("Brak nagłówka X-Tenant-Id.");

    return tenantStore.GetConnectionString(tenantHeader)
        ?? throw new InvalidOperationException($"Tenant '{tenantHeader}' nie znaleziony.");
}
```

### Wariant — connection string z cache (wydajność)

```csharp
public class CachedTenantConnectionStringProvider : IConnectionStringProvider<AppDbContext>
{
    private readonly IMemoryCache cache;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly ITenantStore tenantStore;

    public string GetConnectionString()
    {
        var tenantId = httpContextAccessor.HttpContext?.User.FindFirstValue("tenant_id")
            ?? throw new InvalidOperationException("Brak identyfikatora tenanta.");

        return cache.GetOrCreate($"cs:{tenantId}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return tenantStore.GetConnectionString(tenantId);
        })!;
    }
}
```

---

## IGenericRepository — API

| Metoda | Opis |
|---|---|
| `AsQueryable(filter, orderBy, include, skip, take, tracking)` | Buduje komponowalny `IQueryable<TEntity>`. Brak materializacji. |
| `GetAsync(filter, orderBy, include, skip, take, tracking, ct)` | Materializuje do `List<TEntity>`. |
| `FindAsync(params object[] ids)` | Szuka po kluczu głównym. Obsługuje klucze złożone. |
| `ExistsAsync(predicate, ct)` | Zwraca `true` gdy jakaś encja spełnia predykat. Brak materializacji. |
| `CountAsync(predicate, ct)` | Zlicza encje spełniające predykat (AsNoTracking). |
| `FirstOrDefaultAsync(predicate, orderBy, include, tracking, ct)` | Pierwsza pasująca encja lub null. |
| `Insert(entity)` | Oznacza encję do wstawienia. Wymaga `SaveChangesAsync`. |
| `Insert(IEnumerable)` | Oznacza kolekcję do wstawienia. Wymaga `SaveChangesAsync`. |
| `InsertRangeAsync(entities, bulkConfig, ct)` | Masowe wstawianie przez EFCore.BulkExtensions. **Natychmiastowy zapis.** |
| `InsertOrUpdateAsync(entity, ct)` | Wstaw lub aktualizuj po PK. N+1 per encja. |
| `InsertOrUpdateAsync(IEnumerable, ct)` | Wstaw lub aktualizuj kolekcję po PK. N+1. |
| `InsertNewAsync(IEnumerable, ct)` | Wstawia wyłącznie nowe encje po PK. N+1. |
| `Update(entity)` | Oznacza encję do aktualizacji. Wymaga `SaveChangesAsync`. |
| `Update(IEnumerable)` | Oznacza kolekcję do aktualizacji. Wymaga `SaveChangesAsync`. |
| `UpdateRangeAsync(where, setters, ct)` | Masowa aktualizacja przez `ExecuteUpdateAsync`. **Natychmiastowy zapis.** |
| `Delete(entity)` | Oznacza encję do usunięcia. Wymaga `SaveChangesAsync`. |
| `Delete(IEnumerable)` | Oznacza kolekcję do usunięcia. Wymaga `SaveChangesAsync`. |
| `DeleteRangeAsync(where, ct)` | Masowe usuwanie przez `ExecuteDeleteAsync`. **Natychmiastowy zapis.** |
| `Attach(entity)` | Dołącza encję do kontekstu EF (włącza tracking). |
| `Detach(entity)` | Odłącza encję od kontekstu EF. |

> **Natychmiastowy zapis** = operacja trafia do bazy bez wywoływania `SaveChangesAsync`, z pominięciem change trackera. Nie wywołuje interceptorów EF Core.

---

## IGenericUnitOfWork — API

| Metoda / właściwość | Opis |
|---|---|
| `Context` | Dostęp do bazowego `BaseDbContext` (dla zaawansowanych scenariuszy). |
| `Repository<TEntity>()` | Pobiera lub tworzy cachowane repozytorium. Fail-fast gdy encja nie w modelu. |
| `SaveChangesAsync(ct)` | Zapisuje śledzone zmiany. Zwraca liczbę zapisanych encji. |
| `BeginTransactionAsync(ct)` | Rozpoczyna transakcję. Idempotentna — drugie wywołanie ignorowane. |
| `CommitTransactionAsync(ct)` | Zatwierdza aktywną transakcję. |
| `RollbackTransactionAsync(ct)` | Wycofuje aktywną transakcję. |
| `ExecuteSqlCommandAsync(query, ct)` | Wykonuje surowe polecenie SQL. Zwraca liczbę dotkniętych wierszy. |
| `FromSql<TResult>(query)` | Komponowalny `IQueryable` z surowego SQL. |
| `ReloadAsync<TEntity>(entity, ct)` | Przeładowuje encję z bazy danych, nadpisuje lokalne zmiany. |
| `ExecuteResilientTransactionAsync(action, ct)` | Odporna transakcja z automatycznym retry (ExecutionStrategy). |
| `ExecuteResilientTransactionAsync<T>(action, ct)` | Odporna transakcja zwracająca wynik. |
| `ClearChangeTracker()` | Odłącza wszystkie śledzone encje. Wymagane przed retry. |
| `Dispose()` / `DisposeAsync()` | Zwalnia DbContext i aktywną transakcję. |

---

## Operacje masowe (bulk)

### InsertRangeAsync — EFCore.BulkExtensions

```csharp
var repo = uow.Repository<Product>();

var products = Enumerable.Range(1, 10_000)
    .Select(i => new Product { Id = i, Name = $"P{i}", Price = i * 1.5m, Category = "Bulk" });

await repo.InsertRangeAsync(products);
// Brak SaveChangesAsync — dane są już w bazie danych
```

### UpdateRangeAsync — ExecuteUpdate (bez change trackera)

```csharp
// Dezaktywuj wszystkie produkty w kategorii "Sale" — jeden UPDATE w SQL
int updated = await repo.UpdateRangeAsync(
    p => p.Category == "Sale",
    s => s.SetProperty(p => p.IsActive, false)
          .SetProperty(p => p.Category, "Archive"));
```

> **EF Core 10:** Sygnatura `UpdateRangeAsync` różni się — biblioteka używa `#if NET10_0_OR_GREATER` żeby obsłużyć obie sygnatury (`Action<UpdateSettersBuilder<T>>` w EF10+, `Expression<Func<SetPropertyCalls<T>>>` w EF8/9).

### DeleteRangeAsync — ExecuteDelete (bez change trackera)

```csharp
// Usuń wszystkie nieaktywne produkty — jeden DELETE w SQL
int deleted = await repo.DeleteRangeAsync(p => !p.IsActive);
```

---

## Odporne transakcje

`ExecuteResilientTransactionAsync` automatycznie obsługuje ponowienia przy błędach przejściowych (timeouty, deadlocki) przez `ExecutionStrategy` EF Core.

```csharp
// Wymaga dostawcy z obsługą retry, np. SQL Server z EnableRetryOnFailure
builder.Services.AddPersistence<AppDbContext, AppUnitOfWork>((sp, opt) =>
    opt.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));
```

```csharp
// Akcja wykonana atomowo — automatyczny SaveChanges + Commit lub Rollback
await uow.ExecuteResilientTransactionAsync(async (u, ct) =>
{
    var orders = u.Repository<Order>();
    var products = u.Repository<Product>();

    orders.Insert(new Order { CustomerName = "Kowalski", Total = 250m });
    products.Update(existingProduct);
});
```

Wariant z wynikiem:

```csharp
var orderId = await uow.ExecuteResilientTransactionAsync(async (u, ct) =>
{
    var order = new Order { CustomerName = "Nowak", Total = 100m };
    u.Repository<Order>().Insert(order);
    return order.Id;
});
```

> **Ważne:** Akcja musi być idempotentna — może być ponowiona wielokrotnie. Change tracker jest czyszczony przez `ClearChangeTracker()` przed każdą próbą.

---

## Testowanie

### Testy jednostkowe — EF Core InMemory

```csharp
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString())
    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
    .Options;

using var ctx = new AppDbContext(options);
using var uow = new AppUnitOfWork(ctx);

// Repository<Order> działa — OrderConfiguration jest w assembly
var repo = uow.Repository<Order>();
repo.Insert(new Order { Id = 1, CustomerName = "Test", Total = 100m });
await uow.SaveChangesAsync();

var found = await repo.FindAsync(1);
Assert.NotNull(found);
```

> **Ograniczenie InMemory:** `UpdateRangeAsync` i `DeleteRangeAsync` (ExecuteUpdate/Delete) wymagają relacyjnego dostawcy. Do ich testowania używaj SQLite.

### Testy integracyjne — SQLite in-memory

```csharp
using var connection = new SqliteConnection("DataSource=:memory:");
connection.Open();

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(connection)
    .Options;

using var ctx = new AppDbContext(options);
ctx.Database.EnsureCreated(); // schemat tworzony z IEntityTypeConfiguration<T>
using var uow = new AppUnitOfWork(ctx);

// Wszystkie operacje masowe działają z SQLite
await uow.Repository<Order>().DeleteRangeAsync(o => !o.IsActive);
```

---

## Czasy życia usług

| Usługa | Domyślny czas życia | Powód |
|---|---|---|
| `TDbContext` | Scoped | Jeden DbContext per HTTP request — izolacja change trackera |
| `TUnitOfWork` | Scoped | Współdzielony UoW per request — spójny cache repozytoriów |
| `IGenericUnitOfWork` | Scoped | Alias na `TUnitOfWork` w tym samym scope |
| `IConnectionStringProvider<TDbContext>` | Scoped | Resolwowanie connection stringa per-request dla multi-tenancy |

`AddPersistence` i `AddUnitOfWork` przyjmują opcjonalne parametry `contextLifetime` i `optionsLifetime`.

---

## Przykładowe WebAPI

Projekt `examples/TBJ.Persistence.EfCore.WebApiSample` demonstracja:

- Pełne WebAPI z Swaggerem i wieloma encjami (Produkty, Zamówienia)
- Multi-tenancy — osobna baza per tenant z rozwiązywaniem przez nagłówek `X-Tenant-Id`
- DbContext bez `DbSet<>` — encje tylko przez konfiguracje
- Generyczne repozytoria dostępne dla każdej encji z konfiguracją
- Odporne transakcje — `ExecuteResilientTransactionAsync` w endpointach zamówień
- Operacje masowe — `InsertRangeAsync`, `UpdateRangeAsync`, `DeleteRangeAsync`

```bash
cd examples/TBJ.Persistence.EfCore.WebApiSample
dotnet run
# Swagger UI dostępny pod: https://localhost:7001/swagger
```

Przykładowe wywołanie z nagłówkiem tenanta:

```bash
curl -H "X-Tenant-Id: tenant-A" https://localhost:7001/api/products
curl -H "X-Tenant-Id: tenant-B" https://localhost:7001/api/products
# Każde zapytanie trafia do osobnej bazy danych
```

---

## Wersjonowanie i publikacja NuGet

Projekt używa **MinVer** do automatycznego wersjonowania przez tagi Git:

```bash
git tag v1.0.0
git push origin v1.0.0
# GitHub Actions publikuje automatycznie na NuGet.org i GitHub Packages
```

> Wymaga ustawienia sekretu `NUGET_API_KEY` w ustawieniach repozytorium GitHub.

---

## Licencja

[MIT](LICENSE)  
Autor: [@tbudaj](https://github.com/tbudaj)
