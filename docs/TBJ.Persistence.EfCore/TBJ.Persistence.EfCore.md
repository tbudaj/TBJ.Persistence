# TBJ.Persistence.EfCore

Generyczny wzorzec Repository i Unit of Work zbudowany na Entity Framework Core.  
Niezależny od dostawcy — działa z SQL Server, PostgreSQL (Npgsql), SQLite i każdym innym dostawcą EF Core.  
Obsługuje multi-tenancy przez `IConnectionStringProvider`.

## Spis treści

1. [Instalacja](#instalacja)
2. [Przegląd architektury](#przegl%C4%85d-architektury)
3. [Jak to działa — kluczowy mechanizm](#jak-to-dzia%C5%82a--kluczowy-mechanizm)
4. [Szybki start](#szybki-start)
5. [Rejestracja DI](#rejestracja-di)
   - [Scenariusz zaawansowany — delegat z IConnectionStringProvider](#scenariusz-zaawansowany--delegat-z-iconnectionstringprovider)
   - [Scenariusz prosty — connection string z IConfiguration](#scenariusz-prosty--connection-string-z-iconfiguration)
6. [IGenericRepository — API](#igenericrepository--api)
7. [IGenericUnitOfWork — API](#igenericunitofwork--api)
8. [Operacje masowe (bulk)](#operacje-masowe-bulk)
9. [Odporne transakcje](#odporne-transakcje)
10. [Multi-tenancy](#multi-tenancy)
11. [Testowanie](#testowanie)
12. [Czasy życia usług](#czasy-%C5%BCycia-us%C5%82ug)
13. [Wersjonowanie i publikacja NuGet](#wersjonowanie-i-publikacja-nuget)

---

## Instalacja

```bash
dotnet add package TBJ.Persistence.EfCore
```

> Paczka celuje w `net8.0`, `net9.0` i `net10.0`.  
> **Nie ma zależności od konkretnego dostawcy EF Core** — dodaj paczkę dostawcy osobno.

Przykłady dostawców:

```bash
# SQL Server
dotnet add package Microsoft.EntityFrameworkCore.SqlServer

# PostgreSQL
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL

# SQLite (najczęściej w testach i lokalnym dev)
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

---

## Przegląd architektury

```
IGenericRepository<TEntity>            — CRUD, bulk, komponowalny IQueryable
IGenericUnitOfWork                     — cache repozytoriów, transakcje, SaveChanges
IConnectionStringProvider<TDbContext>  — connection string per-request (multi-tenancy)

BaseDbContext                          — abstrakcyjny DbContext z logowaniem + auto-odkrywanie konfiguracji
GenericRepository<TEntity>             — sealed implementacja IGenericRepository
GenericUnitOfWork<TDbContext>          — abstrakcyjna implementacja IGenericUnitOfWork
PersistenceServiceCollectionExtensions — AddPersistence / AddUnitOfWork
```

**Kluczowe decyzje projektowe:**

| Decyzja | Uzasadnienie |
|---|---|
| Brak domyślnego lazy loading | Jawne `.Include()` — eliminuje niezauważony N+1 |
| `AsNoTracking` domyślnie | Zapytania tylko-do-odczytu bez narzutu change trackera |
| Automatyczne sortowanie po PK przy stronicowaniu | Deterministyczne wyniki bez wymuszania jawnego `OrderBy` |
| Brak domyślnego `Take` | Nie psuje kompozycji LINQ (joiny, podzapytania) |
| Cache repozytoriów per instancja UoW | Wątkobezpieczny `ConcurrentDictionary` — jeden repository per typ encji per scope |
| `GenericUnitOfWork` jest abstrakcyjny | Konsumenci dziedziczą i dodają metody domenowe; DI rejestruje konkretny typ |
| **Brak wymaganych `DbSet<>` w kontekście** | Repozytoria tworzone dynamicznie przez `dbContext.Set<TEntity>()` — zero boilerplate |

---

## Jak to działa — kluczowy mechanizm

To jest najważniejsza rzecz do zrozumienia w tej bibliotece.

### Żadnych DbSet<> — encje przez konfiguracje

Standardowe podejście EF Core wymaga deklarowania właściwości `DbSet<T>` w klasie kontekstu dla każdej encji:

```csharp
// ❌ Tradycyjne podejście — boilerplate, który ta biblioteka eliminuje
public class AppDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<Customer> Customers { get; set; }
    // ... każda nowa encja = nowa właściwość tutaj
}
```

W `TBJ.Persistence.EfCore` działa to inaczej:

```csharp
// ✅ Podejście biblioteki — zero właściwości DbSet<>
public class AppDbContext : BaseDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    // Koniec — żadnych DbSet<>, żadnych OnModelCreating
}
```

### Jak encje trafiają do modelu?

`BaseDbContext.OnModelCreating` automatycznie wywołuje `ApplyConfigurationsFromAssembly(GetType().Assembly)`.  
Oznacza to: **każda klasa implementująca `IEntityTypeConfiguration<TEntity>` w Twoim assembly jest automatycznie odkrywana i rejestruje encję w modelu EF**.

```csharp
// Zdefiniuj konfigurację — to wystarczy żeby encja istniała w modelu
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Total).HasColumnType("decimal(18,2)");
        // ... cała konfiguracja fluent API tutaj
    }
}
```

### Jak repozytorium trafia do encji?

`GenericUnitOfWork.Repository<TEntity>()` wykonuje trzy rzeczy:

1. Sprawdza `dbContext.Model.FindEntityType(typeof(TEntity))` — fail-fast jeśli encja nie istnieje w modelu
2. Woła `dbContext.Set<TEntity>()` — EF Core dynamicznie zwraca `DbSet<TEntity>` dla każdej encji w modelu
3. Cachuje repozytorium w `ConcurrentDictionary<Type, object>` — jeden repozytorium per typ per scope

```
IEntityTypeConfiguration<Order>  ──▶  ApplyConfigurationsFromAssembly
                                              │
                                              ▼
                                   Model EF Core wie o Order
                                              │
                                              ▼
uow.Repository<Order>()  ──▶  dbContext.Set<Order>()  ──▶  GenericRepository<Order>
```

**Wynik:** Dodanie nowej encji do projektu wymaga tylko jednej klasy `IEntityTypeConfiguration<TEntity>`. Kontekst i UoW nie wymagają żadnych zmian.

---

## Szybki start

### 1. Zdefiniuj encję i jej konfigurację

```csharp
// Encja domenowa — zwykły POCO
public class Order
{
    public int Id { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Konfiguracja EF Core — to jest jedyne co trzeba napisać żeby encja była w modelu
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Total).HasColumnType("decimal(18,2)");
        builder.Property(o => o.CreatedAt).IsRequired();
    }
}
```

### 2. Zdefiniuj DbContext — bez DbSet<>

```csharp
using TBJ.Persistence.EfCore.Implementation;

// Cała klasa kontekstu — żadnych właściwości DbSet<>, żadnych nadpisań OnModelCreating
public class AppDbContext : BaseDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}
```

`BaseDbContext` automatycznie odkrywa wszystkie `IEntityTypeConfiguration<T>` z tego assembly przez `ApplyConfigurationsFromAssembly`.

### 3. Utwórz UnitOfWork — jeden konstruktor

```csharp
using TBJ.Persistence.EfCore.Implementation;

// Cała klasa UoW — tylko konstruktor przekazujący do bazy
public class AppUnitOfWork : GenericUnitOfWork<AppDbContext>
{
    public AppUnitOfWork(AppDbContext dbContext, ILoggerFactory? loggerFactory = null)
        : base(dbContext, loggerFactory) { }
}
```

### 4. Zarejestruj w DI i użyj

```csharp
// Program.cs — jedna linia rejestracji
builder.Services.AddPersistence<AppDbContext, AppUnitOfWork>(
    builder.Configuration,
    "Default",
    (opt, cs) => opt.UseSqlServer(cs));
```

```csharp
// Klasa serwisu — Repository<T> dostępne dla każdej encji z konfiguracją
public class OrderService(IGenericUnitOfWork uow)
{
    public async Task<Order?> GetOrderAsync(int id)
        => await uow.Repository<Order>().FindAsync(id);

    public async Task CreateOrderAsync(Order order)
    {
        uow.Repository<Order>().Insert(order);
        await uow.SaveChangesAsync();
    }
}
```

> **Kluczowe:** `uow.Repository<Order>()` działa bez żadnej wcześniejszej rejestracji `Order` w UoW ani w DbContext.  
> Wystarczy, że w assembly istnieje `OrderConfiguration : IEntityTypeConfiguration<Order>`.

---

## Rejestracja DI

### Scenariusz zaawansowany — delegat z IConnectionStringProvider

Użyj tego przeciążenia gdy connection string musi być resolwowany per-request (np. aplikacje multi-tenant).

```csharp
// Zarejestruj własnego dostawcę
builder.Services.AddScoped<IConnectionStringProvider<AppDbContext>, TenantConnectionStringProvider>();

// Zarejestruj persystencję — delegat otrzymuje IServiceProvider
builder.Services.AddPersistence<AppDbContext, AppUnitOfWork>((serviceProvider, opt) =>
{
    var provider = serviceProvider.GetRequiredService<IConnectionStringProvider<AppDbContext>>();
    opt.UseSqlServer(provider.GetConnectionString());
});
```

**Przykładowa implementacja `IConnectionStringProvider`:**

```csharp
public class TenantConnectionStringProvider : IConnectionStringProvider<AppDbContext>
{
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly ITenantRepository tenantRepository;

    public TenantConnectionStringProvider(
        IHttpContextAccessor httpContextAccessor,
        ITenantRepository tenantRepository)
    {
        this.httpContextAccessor = httpContextAccessor;
        this.tenantRepository = tenantRepository;
    }

    public string GetConnectionString()
    {
        var tenantId = httpContextAccessor.HttpContext?.User.FindFirstValue("tenant_id")
            ?? throw new InvalidOperationException("Identyfikator tenanta nie znaleziony w JWT.");

        return tenantRepository.GetConnectionString(tenantId);
    }
}
```

### Scenariusz prosty — connection string z IConfiguration

Użyj tego przeciążenia dla aplikacji single-tenant lub gdy connection string jest statyczny.

```json
// appsettings.json
{
  "ConnectionStrings": {
    "Default": "Server=.;Database=AppDb;Trusted_Connection=True;"
  }
}
```

```csharp
// Program.cs
builder.Services.AddPersistence<AppDbContext, AppUnitOfWork>(
    builder.Configuration,
    "Default",
    (opt, connectionString) => opt.UseSqlServer(connectionString));
```

Jeśli DbContext jest już zarejestrowany osobno — rejestruj tylko UoW:

```csharp
services.AddUnitOfWork<AppUnitOfWork>();
```

---

## IGenericRepository — API

| Metoda | Opis |
|---|---|
| `AsQueryable(filter, orderBy, include, skip, take, tracking)` | Buduje komponowalny `IQueryable<TEntity>`. Brak materializacji. |
| `GetAsync(filter, orderBy, include, skip, take, tracking, ct)` | Materializuje do `List<TEntity>`. |
| `FindAsync(params object[] ids)` | Szuka po kluczu głównym. Obsługuje klucze złożone. |
| `ExistsAsync(predicate, ct)` | Zwraca `true` gdy jakaś encja spełnia predykat. |
| `CountAsync(predicate, ct)` | Zlicza encje spełniające predykat. |
| `FirstOrDefaultAsync(predicate, orderBy, include, tracking, ct)` | Pierwsza pasująca encja lub null. |
| `Insert(entity)` | Oznacza encję do wstawienia. Wymaga `SaveChangesAsync`. |
| `Insert(IEnumerable)` | Oznacza kolekcję do wstawienia. Wymaga `SaveChangesAsync`. |
| `InsertRangeAsync(entities, bulkConfig, ct)` | Masowe wstawianie przez EFCore.BulkExtensions. Natychmiastowy zapis. |
| `InsertOrUpdateAsync(entity, ct)` | Wstaw lub aktualizuj po PK. N+1 dla kolekcji. |
| `InsertOrUpdateAsync(IEnumerable, ct)` | Wstaw lub aktualizuj kolekcję po PK. N+1. |
| `InsertNewAsync(IEnumerable, ct)` | Wstawia wyłącznie nowe encje po PK. N+1. |
| `Update(entity)` | Oznacza encję do aktualizacji. Wymaga `SaveChangesAsync`. |
| `Update(IEnumerable)` | Oznacza kolekcję do aktualizacji. Wymaga `SaveChangesAsync`. |
| `UpdateRangeAsync(where, setters, ct)` | Masowa aktualizacja przez `ExecuteUpdateAsync`. Natychmiastowy zapis. |
| `Delete(entity)` | Oznacza encję do usunięcia. Wymaga `SaveChangesAsync`. |
| `Delete(IEnumerable)` | Oznacza kolekcję do usunięcia. Wymaga `SaveChangesAsync`. |
| `DeleteRangeAsync(where, ct)` | Masowe usuwanie przez `ExecuteDeleteAsync`. Natychmiastowy zapis. |
| `Attach(entity)` | Dołącza encję do kontekstu EF (włącza tracking). |
| `Detach(entity)` | Odłącza encję od kontekstu EF. |

---

## IGenericUnitOfWork — API

| Metoda / właściwość | Opis |
|---|---|
| `Context` | Dostęp do bazowego `BaseDbContext`. |
| `Repository<TEntity>()` | Pobiera lub tworzy cachowane repozytorium dla typu encji. |
| `SaveChangesAsync(ct)` | Zapisuje śledzone zmiany. Zwraca liczbę zapisanych encji. |
| `BeginTransactionAsync(ct)` | Rozpoczyna transakcję bazodanową. Idempotentna. |
| `CommitTransactionAsync(ct)` | Zatwierdza aktywną transakcję. Brak operacji gdy brak transakcji. |
| `RollbackTransactionAsync(ct)` | Wycofuje aktywną transakcję. Brak operacji gdy brak transakcji. |
| `ExecuteSqlCommandAsync(query, ct)` | Wykonuje surowe polecenie SQL. Zwraca liczbę dotkniętych wierszy. |
| `FromSql<TResult>(query)` | Komponowalny `IQueryable` z surowego SQL. |
| `ReloadAsync<TEntity>(entity, ct)` | Przeładowuje encję z bazy danych. |
| `ExecuteResilientTransactionAsync(action, ct)` | Odporna transakcja z automatycznym ponawianiem (ExecutionStrategy). |
| `ExecuteResilientTransactionAsync<T>(action, ct)` | Odporna transakcja zwracająca wynik. |
| `ClearChangeTracker()` | Odłącza wszystkie śledzone encje. |
| `Dispose()` / `DisposeAsync()` | Zwalnia DbContext i aktywną transakcję. |

---

## Operacje masowe (bulk)

`InsertRangeAsync` używa **EFCore.BulkExtensions** do efektywnego wstawiania dużych zbiorów danych z pominięciem change trackera.

```csharp
var repo = uow.Repository<Product>();

var products = Enumerable.Range(1, 10_000)
    .Select(i => new Product { Id = i, Name = $"P{i}", Price = i });

await repo.InsertRangeAsync(products);
// Brak SaveChangesAsync — dane są już w bazie danych
```

`UpdateRangeAsync` i `DeleteRangeAsync` delegują do natywnych metod EF Core `ExecuteUpdateAsync`/`ExecuteDeleteAsync`:

```csharp
// Dezaktywuj wszystkie produkty w kategorii "Sale"
await repo.UpdateRangeAsync(
    p => p.Category == "Sale",
    s => s.SetProperty(p => p.IsActive, false));

// Usuń wszystkie nieaktywne produkty
await repo.DeleteRangeAsync(p => !p.IsActive);
```

> **Uwaga:** Operacje masowe omijają change tracker i nie wywołują interceptorów EF Core ani zdarzeń `SaveChanges`.

> **EF Core 10:** Sygnatura `UpdateRangeAsync` różni się między wersjami EF Core — biblioteka używa `#if NET10_0_OR_GREATER` żeby obsłużyć obie sygnatury (`Action<UpdateSettersBuilder<T>>` w EF10, `Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>>` w EF8/9).

---

## Odporne transakcje

Użyj `ExecuteResilientTransactionAsync` do automatycznego ponawiania błędów przejściowych przez `ExecutionStrategy` EF Core (dostępna dla dostawców obsługujących retry, np. `UseSqlServer` z `EnableRetryOnFailure`).

```csharp
await uow.ExecuteResilientTransactionAsync(async (u, ct) =>
{
    var repo = u.Repository<Order>();
    repo.Insert(new Order { /* ... */ });
});
// SaveChangesAsync i CommitTransactionAsync wywoływane automatycznie
```

Wariant zwracający wartość:

```csharp
var orderId = await uow.ExecuteResilientTransactionAsync(async (u, ct) =>
{
    var repo = u.Repository<Order>();
    var order = new Order { /* ... */ };
    repo.Insert(order);
    return order.Id;
});
```

> **Ważne:** Akcja musi być idempotentna — może być ponawiana wielokrotnie. Change tracker jest czyszczony przed każdą próbą przez `ClearChangeTracker()`.

---

## Multi-tenancy

Zarejestruj własny `IConnectionStringProvider<TDbContext>` i użyj przeciążenia delegatowego `AddPersistence`:

```csharp
// 1. Zaimplementuj IConnectionStringProvider<TDbContext>
public class TenantProvider : IConnectionStringProvider<AppDbContext>
{
    // ... resolwuj connection string z JWT / cache / bazy danych
    public string GetConnectionString() => /* connection string per-tenant */;
}

// 2. Zarejestruj
builder.Services.AddScoped<IConnectionStringProvider<AppDbContext>, TenantProvider>();
builder.Services.AddPersistence<AppDbContext, AppUnitOfWork>((sp, opt) =>
{
    var provider = sp.GetRequiredService<IConnectionStringProvider<AppDbContext>>();
    opt.UseSqlServer(provider.GetConnectionString());
});
```

Interfejs `IConnectionStringProvider` jest celowo minimalny — kontrolujesz całą logikę resolwowania tenanta.

---

## Testowanie

### Testy jednostkowe — EF Core InMemory

Nie potrzebujesz żadnej bazy danych — InMemory provider wystarczy do testowania logiki repozytorium.

```csharp
// Kontekst bez DbSet<> — wystarczy konfiguracja encji w assembly
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString())
    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
    .Options;

using var ctx = new AppDbContext(options);
using var uow = new AppUnitOfWork(ctx);

// Repository<Order> dostępne bo OrderConfiguration istnieje w assembly
var repo = uow.Repository<Order>();
repo.Insert(new Order { Id = 1, Total = 100m });
await uow.SaveChangesAsync();

var found = await repo.FindAsync(1);
Assert.NotNull(found);
```

> **Ograniczenie InMemory:** Dostawca InMemory nie obsługuje `ExecuteUpdateAsync` / `ExecuteDeleteAsync` (metody `UpdateRangeAsync`/`DeleteRangeAsync`). Dla tych operacji używaj testów integracyjnych z SQLite.

### Testy integracyjne — SQLite in-memory

SQLite in-memory uruchamia prawdziwy silnik SQL bez wymagań serwerowych — idealny do testowania operacji masowych i transakcji.

```csharp
using var connection = new SqliteConnection("DataSource=:memory:");
connection.Open();

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(connection)
    .Options;

using var ctx = new AppDbContext(options);
ctx.Database.EnsureCreated(); // tworzy schemat — encje odkryte przez IEntityTypeConfiguration<T>
using var uow = new AppUnitOfWork(ctx);

var repo = uow.Repository<Order>();
repo.Insert(new Order { Id = 1, Total = 50m });
await uow.SaveChangesAsync();

// Operacje masowe działają z SQLite
await repo.DeleteRangeAsync(o => o.Total < 100m);
Assert.Equal(0, await repo.CountAsync());
```

---

## Czasy życia usług

| Usługa | Domyślny czas życia | Powód |
|---|---|---|
| `TDbContext` | Scoped | Jeden DbContext per HTTP request — izolacja change trackera |
| `TUnitOfWork` | Scoped | Współdzielony UoW per request — spójny cache repozytoriów |
| `IGenericUnitOfWork` | Scoped | Alias na `TUnitOfWork` w tym samym scope |
| `IConnectionStringProvider<TDbContext>` | Scoped | Resolwowanie connection stringa per-request dla multi-tenancy |

`AddPersistence` i `AddUnitOfWork` przyjmują opcjonalne parametry `contextLifetime` i `optionsLifetime` do zmiany tych domyślnych wartości.

---

## Wersjonowanie i publikacja NuGet

Projekt używa **MinVer** do automatycznego wersjonowania przez tagi Git:

```bash
# Utwórz tag wersji — pipeline GitHub Actions publikuje automatycznie
git tag v1.0.0
git push origin v1.0.0
```

Przepływ publikacji:

1. Tag `v*` uruchamia workflow `release.yml`
2. Build + testy dla `net8.0`, `net9.0`, `net10.0`
3. `dotnet pack` generuje `.nupkg`
4. Publikacja na NuGet.org i GitHub Packages

> Dostęp do NuGet.org wymaga ustawienia sekretu `NUGET_API_KEY` w ustawieniach repozytorium GitHub.
