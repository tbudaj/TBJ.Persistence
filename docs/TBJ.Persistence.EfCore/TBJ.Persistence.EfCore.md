# TBJ.Persistence.EfCore

Generic Repository and Unit of Work pattern built on top of Entity Framework Core.  
Provider-agnostic — works with SQL Server, PostgreSQL (Npgsql), SQLite and any other EF Core provider.  
Supports multi-tenancy via `IConnectionStringProvider`.

## Table of contents

1. [Installation](#installation)
2. [Architecture overview](#architecture-overview)
3. [Quick start](#quick-start)
4. [DI registration](#di-registration)
   - [Advanced scenario — delegate with IConnectionStringProvider](#advanced-scenario--delegate-with-iconnectionstringprovider)
   - [Simple scenario — connection string from IConfiguration](#simple-scenario--connection-string-from-iconfiguration)
5. [IGenericRepository API reference](#igenericrepository-api-reference)
6. [IGenericUnitOfWork API reference](#igenericunitofwork-api-reference)
7. [Bulk operations](#bulk-operations)
8. [Resilient transactions](#resilient-transactions)
9. [Multi-tenancy](#multi-tenancy)
10. [Testing](#testing)
11. [Service lifetimes](#service-lifetimes)
12. [Versioning and NuGet release](#versioning-and-nuget-release)

---

## Installation

```bash
dotnet add package TBJ.Persistence.EfCore
```

> The package targets `net8.0`, `net9.0` and `net10.0`.  
> It has **no dependency on a specific EF Core provider** — add the provider package separately.

Provider examples:

```bash
# SQL Server
dotnet add package Microsoft.EntityFrameworkCore.SqlServer

# PostgreSQL
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL

# SQLite (commonly used for tests/dev)
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

---

## Architecture overview

```
IGenericRepository<TEntity>          — CRUD, bulk, composable IQueryable
IGenericUnitOfWork                   — repository cache, transactions, SaveChanges
IConnectionStringProvider<TDbContext>— per-request connection string (multi-tenancy)

BaseDbContext                        — abstract DbContext with logging + auto-configuration discovery
GenericRepository<TEntity>           — sealed implementation of IGenericRepository
GenericUnitOfWork<TDbContext>        — abstract implementation of IGenericUnitOfWork
PersistenceServiceCollectionExtensions — AddPersistence / AddUnitOfWork
```

**Key design decisions:**

| Decision | Rationale |
|---|---|
| No default lazy loading | Use explicit `.Include()` — prevents N+1 surprises |
| `AsNoTracking` by default | Read-only queries avoid change tracker overhead |
| Auto PK ordering on paging | Deterministic results without requiring explicit `OrderBy` |
| No default `Take` | Prevents breaking composable LINQ (joins, sub-queries) |
| Repository cache per UoW instance | Thread-safe `ConcurrentDictionary` — single repository per entity type per scope |
| Abstract `GenericUnitOfWork` | Consumers inherit and add domain-specific methods; DI registers the concrete type |

---

## Quick start

### 1. Define your DbContext

```csharp
using TBJ.Persistence.EfCore.Implementation;

public class AppDbContext : BaseDbContext
{
    public DbSet<Order> Orders => Set<Order>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}
```

Entity configurations are auto-discovered from the assembly via `ApplyConfigurationsFromAssembly`.

### 2. Create your UnitOfWork

```csharp
using TBJ.Persistence.EfCore.Implementation;

public class AppUnitOfWork : GenericUnitOfWork<AppDbContext>
{
    public AppUnitOfWork(AppDbContext dbContext, ILoggerFactory? loggerFactory = null)
        : base(dbContext, loggerFactory) { }
}
```

That's it — no boilerplate repository classes needed.

### 3. Register in DI and use

```csharp
// Program.cs
builder.Services.AddPersistence<AppDbContext, AppUnitOfWork>(
    builder.Configuration,
    "Default",
    (opt, cs) => opt.UseSqlServer(cs));
```

```csharp
// Service class
public class OrderService(IGenericUnitOfWork uow)
{
    public async Task<Order?> GetOrderAsync(int id)
    {
        var repo = uow.Repository<Order>();
        return await repo.FindAsync(id);
    }

    public async Task CreateOrderAsync(Order order)
    {
        var repo = uow.Repository<Order>();
        repo.Insert(order);
        await uow.SaveChangesAsync();
    }
}
```

---

## DI registration

### Advanced scenario — delegate with IConnectionStringProvider

Use this overload when the connection string must be resolved per-request (e.g. multi-tenant applications).

```csharp
// Register your custom provider
builder.Services.AddScoped<IConnectionStringProvider<AppDbContext>, TenantConnectionStringProvider>();

// Register persistence — the delegate receives IServiceProvider
builder.Services.AddPersistence<AppDbContext, AppUnitOfWork>((serviceProvider, opt) =>
{
    var provider = serviceProvider.GetRequiredService<IConnectionStringProvider<AppDbContext>>();
    opt.UseSqlServer(provider.GetConnectionString());
});
```

**`IConnectionStringProvider` implementation example:**

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
            ?? throw new InvalidOperationException("Tenant identifier not found in JWT.");

        return tenantRepository.GetConnectionString(tenantId);
    }
}
```

### Simple scenario — connection string from IConfiguration

Use this overload for single-tenant applications or when the connection string is static.

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

If you need to register the UoW separately (DbContext already registered elsewhere):

```csharp
services.AddUnitOfWork<AppUnitOfWork>();
```

---

## IGenericRepository API reference

| Method | Description |
|---|---|
| `AsQueryable(filter, orderBy, include, skip, take, tracking)` | Builds a composable `IQueryable<TEntity>`. No materialization. |
| `GetAsync(filter, orderBy, include, skip, take, tracking, ct)` | Materializes to `List<TEntity>`. |
| `FindAsync(params object[] ids)` | Finds by primary key. Supports composite keys. |
| `ExistsAsync(predicate, ct)` | Returns `true` if any entity matches. |
| `CountAsync(predicate, ct)` | Returns count of matching entities. |
| `FirstOrDefaultAsync(predicate, orderBy, include, tracking, ct)` | Returns first matching entity or null. |
| `Insert(entity)` | Marks entity for insertion. Requires `SaveChangesAsync`. |
| `Insert(IEnumerable)` | Marks collection for insertion. Requires `SaveChangesAsync`. |
| `InsertRangeAsync(entities, bulkConfig, ct)` | Bulk insert via EFCore.BulkExtensions. Immediate write. |
| `InsertOrUpdateAsync(entity, ct)` | Insert or update by PK. N+1 for collections. |
| `InsertOrUpdateAsync(IEnumerable, ct)` | Insert or update collection by PK. N+1. |
| `InsertNewAsync(IEnumerable, ct)` | Inserts only new entities by PK. N+1. |
| `Update(entity)` | Marks entity for update. Requires `SaveChangesAsync`. |
| `Update(IEnumerable)` | Marks collection for update. Requires `SaveChangesAsync`. |
| `UpdateRangeAsync(where, setters, ct)` | Bulk update via `ExecuteUpdateAsync`. Immediate write. |
| `Delete(entity)` | Marks entity for deletion. Requires `SaveChangesAsync`. |
| `Delete(IEnumerable)` | Marks collection for deletion. Requires `SaveChangesAsync`. |
| `DeleteRangeAsync(where, ct)` | Bulk delete via `ExecuteDeleteAsync`. Immediate write. |
| `Attach(entity)` | Attaches entity to EF context for tracking. |
| `Detach(entity)` | Detaches entity from EF context. |

---

## IGenericUnitOfWork API reference

| Method / Property | Description |
|---|---|
| `Context` | Access to the underlying `BaseDbContext`. |
| `Repository<TEntity>()` | Gets or creates a cached repository for the entity type. |
| `SaveChangesAsync(ct)` | Saves tracked changes. Returns number of saved entities. |
| `BeginTransactionAsync(ct)` | Starts a database transaction. Idempotent. |
| `CommitTransactionAsync(ct)` | Commits the active transaction. No-op if none. |
| `RollbackTransactionAsync(ct)` | Rolls back the active transaction. No-op if none. |
| `ExecuteSqlCommandAsync(query, ct)` | Executes raw SQL command. Returns affected rows. |
| `FromSql<TResult>(query)` | Composable `IQueryable` from raw SQL. |
| `ReloadAsync<TEntity>(entity, ct)` | Reloads entity from database. |
| `ExecuteResilientTransactionAsync(action, ct)` | Resilient retry transaction (ExecutionStrategy). |
| `ExecuteResilientTransactionAsync<T>(action, ct)` | Resilient retry transaction returning a result. |
| `ClearChangeTracker()` | Detaches all tracked entities. |
| `Dispose()` / `DisposeAsync()` | Releases DbContext and active transaction. |

---

## Bulk operations

`InsertRangeAsync` uses **EFCore.BulkExtensions** to insert large data sets efficiently, bypassing the EF change tracker.

```csharp
var products = Enumerable.Range(1, 10_000)
    .Select(i => new Product { Id = i, Name = $"P{i}", Price = i });

var repo = uow.Repository<Product>();
await repo.InsertRangeAsync(products);
// No SaveChangesAsync needed — data is already in the database
```

For bulk updates and deletes, use `UpdateRangeAsync` and `DeleteRangeAsync` which delegate to EF Core's native `ExecuteUpdateAsync`/`ExecuteDeleteAsync`:

```csharp
// Deactivate all products in category "Sale"
await repo.UpdateRangeAsync(
    p => p.Category == "Sale",
    s => s.SetProperty(p => p.IsActive, false));

// Remove all inactive products
await repo.DeleteRangeAsync(p => !p.IsActive);
```

> **Note:** Bulk operations bypass the change tracker and do not trigger EF Core interceptors or `SaveChanges` events.

---

## Resilient transactions

Use `ExecuteResilientTransactionAsync` to automatically retry transient failures using EF Core's `ExecutionStrategy` (enabled by resilience-aware providers such as `UseSqlServer` with `EnableRetryOnFailure`).

```csharp
await uow.ExecuteResilientTransactionAsync(async (u, ct) =>
{
    var repo = u.Repository<Order>();
    repo.Insert(new Order { /* ... */ });
});
// SaveChangesAsync and CommitTransactionAsync are called automatically
```

Returning a value:

```csharp
var orderId = await uow.ExecuteResilientTransactionAsync(async (u, ct) =>
{
    var repo = u.Repository<Order>();
    var order = new Order { /* ... */ };
    repo.Insert(order);
    return order.Id;
});
```

> **Important:** The action must be idempotent — it may be retried multiple times. The change tracker is cleared before each attempt via `ClearChangeTracker()`.

---

## Multi-tenancy

Register a custom `IConnectionStringProvider<TDbContext>` and use the delegate overload of `AddPersistence`:

```csharp
// 1. Implement IConnectionStringProvider<TDbContext>
public class TenantProvider : IConnectionStringProvider<AppDbContext>
{
    // ... resolve connection string from JWT / cache / database
    public string GetConnectionString() => /* per-tenant connection string */;
}

// 2. Register
builder.Services.AddScoped<IConnectionStringProvider<AppDbContext>, TenantProvider>();
builder.Services.AddPersistence<AppDbContext, AppUnitOfWork>((sp, opt) =>
{
    var provider = sp.GetRequiredService<IConnectionStringProvider<AppDbContext>>();
    opt.UseSqlServer(provider.GetConnectionString());
});
```

The `IConnectionStringProvider` interface is intentionally minimal — you control all tenant-resolution logic.

---

## Testing

### Unit tests — EF Core InMemory

```csharp
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString())
    .Options;

using var ctx = new AppDbContext(options);
using var uow = new AppUnitOfWork(ctx);

var repo = uow.Repository<Order>();
repo.Insert(new Order { Id = 1, Total = 100m });
await uow.SaveChangesAsync();

var found = await repo.FindAsync(1);
Assert.NotNull(found);
```

### Integration tests — SQLite in-memory

```csharp
using var connection = new SqliteConnection("DataSource=:memory:");
connection.Open();

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(connection)
    .Options;

using var ctx = new AppDbContext(options);
ctx.Database.EnsureCreated();

using var uow = new AppUnitOfWork(ctx);
// ... run tests with real SQL engine
```

> SQLite in-memory supports **real transactions** (`BeginTransaction` / `Rollback`) — unlike the InMemory provider.

---

## Service lifetimes

| Service | Lifetime |
|---|---|
| `TDbContext` (your DbContext) | `Scoped` (default) |
| `TUnitOfWork` (your UoW) | `Scoped` |
| `IGenericUnitOfWork` | `Scoped` (same instance as concrete UoW) |
| `IConnectionStringProvider<TDbContext>` | `Scoped` (recommended — one per request) |
| `GenericRepository<TEntity>` | Created and cached per UoW instance |

---

## Versioning and NuGet release

The package version is managed by **MinVer** using git tags.  
Create a release tag to trigger the CI/CD release workflow:

```bash
git tag efcore/v1.0.0
git push origin efcore/v1.0.0
```

The `release.yml` workflow will:
1. Build and pack the NuGet package
2. Push to **NuGet.org**
3. Push to **GitHub Packages**
4. Create a GitHub Release with auto-generated notes
