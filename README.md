# TBJ.Persistence.EfCore

Generic Repository and Unit of Work pattern built on Entity Framework Core.  
Provider-agnostic — works with SQL Server, PostgreSQL (Npgsql), SQLite, and any other EF Core provider.  
Supports multi-tenancy (separate database per tenant) via `IConnectionStringProvider`.

[![build](https://github.com/tbudaj/TBJ.Persistence/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/tbudaj/TBJ.Persistence/actions/workflows/build-and-test.yml)
[![NuGet](https://img.shields.io/nuget/v/TBJ.Persistence.EfCore)](https://www.nuget.org/packages/TBJ.Persistence.EfCore)

## Table of Contents

1. [Installation](#installation)
2. [What makes this library stand out](#what-makes-this-library-stand-out)
3. [How it works — the key mechanism](#how-it-works--the-key-mechanism)
4. [Quick start](#quick-start)
5. [DI registration](#di-registration)
   - [Simple scenario — connection string from IConfiguration](#simple-scenario--connection-string-from-iconfiguration)
   - [Advanced scenario — multi-tenancy with IConnectionStringProvider](#advanced-scenario--multi-tenancy-with-iconnectionstringprovider)
6. [Multi-tenancy — separate database per tenant](#multi-tenancy--separate-database-per-tenant)
7. [IGenericRepository — API](#igenericrepository--api)
8. [IGenericUnitOfWork — API](#igenericunitofwork--api)
9. [Bulk operations](#bulk-operations)
10. [Resilient transactions](#resilient-transactions)
11. [Testing](#testing)
12. [Service lifetimes](#service-lifetimes)
13. [Sample WebAPI](#sample-webapi)
14. [Versioning and NuGet publishing](#versioning-and-nuget-publishing)

---

## Installation

```bash
dotnet add package TBJ.Persistence.EfCore
```

> The package targets `net8.0`, `net9.0`, and `net10.0`.  
> **There is no dependency on any specific EF Core provider** — add the provider package separately.

```bash
# SQL Server
dotnet add package Microsoft.EntityFrameworkCore.SqlServer

# PostgreSQL
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL

# SQLite (most commonly used in tests and local dev)
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

---

## What makes this library stand out

### No DbSet\<T\> in DbContext

The classic EF Core approach requires declaring a `DbSet<T>` for every entity. This library eliminates that.  
Entities are registered in the model **automatically** via `IEntityTypeConfiguration<T>` configuration classes.

```csharp
// ❌ Traditional approach — boilerplate per entity
public class AppDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<Customer> Customers { get; set; }
    // every new entity = a change to the context
}

// ✅ This library's approach — zero DbSet<> properties
public class AppDbContext : BaseDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    // That's it. Adding a new entity only requires a new IEntityTypeConfiguration<T> class.
}
```

### Generic repositories — zero repository classes

You never write `OrderRepository`, `ProductRepository`, etc. A repository for any entity is retrieved in a single line:

```csharp
var repo = uow.Repository<Order>();     // ready
var repo = uow.Repository<Product>();   // ready
var repo = uow.Repository<Customer>();  // ready
// no code to write between a new entity and its first query
```

### Multi-tenancy with a separate database per tenant

`IConnectionStringProvider<TDbContext>` is resolved from DI per-request — each request can receive a different connection string based on a JWT claim, an HTTP header, a tenant cache, or any other custom logic.

### Bulk operations without the change tracker

`InsertRangeAsync` (via EFCore.BulkExtensions), `UpdateRangeAsync`, and `DeleteRangeAsync` (via EF Core's native `ExecuteUpdate`/`ExecuteDelete`) — no `SaveChanges`, no change-tracker overhead.

### Resilient transactions with automatic retry

`ExecuteResilientTransactionAsync` wraps an action in EF Core's `ExecutionStrategy` — automatic retries on transient failures (e.g. SQL Server `EnableRetryOnFailure`).

---

## How it works — the key mechanism

### Entity registration flow

```
IEntityTypeConfiguration<Order>        <- the only thing you write per entity
         │
         ▼
BaseDbContext.OnModelCreating()
-> ApplyConfigurationsFromAssembly()   <- auto-discovery from the context's assembly
         │
         ▼
EF Core model knows about Order
         │
         ▼
uow.Repository<Order>()
-> dbContext.Set<Order>()              <- dynamic DbSet without a property on the class
-> GenericRepository<Order>            <- cached in ConcurrentDictionary per scope
```

### What Repository\<T\>() validates

Before creating a repository, the following check is performed:

```csharp
if (dbContext.Model.FindEntityType(typeof(TEntity)) is null)
    throw new InvalidOperationException("Entity type not found in the model.");
```

**Fail-fast** — if you forget to write `OrderConfiguration`, you get a clear exception the first time you call `Repository<Order>()`, not when a query is sent to the database.

---

## Quick start

### Step 1 — entity and configuration (the only required files)

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

### Step 2 — DbContext without DbSet\<\>

```csharp
// Infrastructure/AppDbContext.cs
public class AppDbContext : BaseDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    // No DbSet<T> properties. BaseDbContext.OnModelCreating discovers OrderConfiguration automatically.
}
```

### Step 3 — UnitOfWork with a single constructor

```csharp
// Infrastructure/AppUnitOfWork.cs
public class AppUnitOfWork : GenericUnitOfWork<AppDbContext>
{
    public AppUnitOfWork(AppDbContext dbContext, ILoggerFactory? loggerFactory = null)
        : base(dbContext, loggerFactory) { }
}
```

### Step 4 — registration and usage

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
// OrderService.cs — consume IGenericUnitOfWork
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

## DI registration

### Simple scenario — connection string from IConfiguration

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

When the DbContext is already registered by another mechanism — register only the UoW:

```csharp
services.AddUnitOfWork<AppUnitOfWork>();
```

### Advanced scenario — multi-tenancy with IConnectionStringProvider

```csharp
// Register a per-request connection string provider
builder.Services.AddScoped<IConnectionStringProvider<AppDbContext>, TenantConnectionStringProvider>();

// The delegate receives an IServiceProvider — resolve the connection string from your own provider
builder.Services.AddPersistence<AppDbContext, AppUnitOfWork>((serviceProvider, opt) =>
{
    var provider = serviceProvider.GetRequiredService<IConnectionStringProvider<AppDbContext>>();
    opt.UseSqlServer(provider.GetConnectionString());
});
```

---

## Multi-tenancy — separate database per tenant

Pattern: each tenant has its own database. The connection string is resolved based on a tenant identifier from a JWT claim or an HTTP header.

### 1. Implementing IConnectionStringProvider

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
    /// Resolves the connection string for the current tenant from the JWT claim "tenant_id".
    /// Called once per Scoped lifetime (one DbContext per HTTP request).
    /// </summary>
    public string GetConnectionString()
    {
        var tenantId = httpContextAccessor.HttpContext?.User.FindFirstValue("tenant_id")
            ?? throw new InvalidOperationException("Tenant identifier not found in JWT.");

        return tenantStore.GetConnectionString(tenantId)
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' not found.");
    }
}
```

### 2. Registration

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

### 3. Usage — transparent to the application layer

```csharp
// The application service knows nothing about multi-tenancy — it works identically to single-tenant
public class OrderService(IGenericUnitOfWork uow)
{
    // Every call operates on the database of the tenant from the current request
    public async Task<List<Order>> GetOrdersAsync()
        => await uow.Repository<Order>().GetAsync();
}
```

### Variant — connection string from an HTTP header

```csharp
public string GetConnectionString()
{
    var tenantHeader = httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].ToString();
    if (string.IsNullOrEmpty(tenantHeader))
        throw new InvalidOperationException("Missing X-Tenant-Id header.");

    return tenantStore.GetConnectionString(tenantHeader)
        ?? throw new InvalidOperationException($"Tenant '{tenantHeader}' not found.");
}
```

### Variant — connection string from cache (performance)

```csharp
public class CachedTenantConnectionStringProvider : IConnectionStringProvider<AppDbContext>
{
    private readonly IMemoryCache cache;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly ITenantStore tenantStore;

    public string GetConnectionString()
    {
        var tenantId = httpContextAccessor.HttpContext?.User.FindFirstValue("tenant_id")
            ?? throw new InvalidOperationException("Tenant identifier not found.");

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

| Method | Description |
|---|---|
| `AsQueryable(filter, orderBy, include, skip, take, tracking)` | Builds a composable `IQueryable<TEntity>`. No materialisation. |
| `GetAsync(filter, orderBy, include, skip, take, tracking, ct)` | Materialises to `List<TEntity>`. |
| `FindAsync(params object[] ids)` | Looks up by primary key. Supports composite keys. |
| `ExistsAsync(predicate, ct)` | Returns `true` if any entity satisfies the predicate. No materialisation. |
| `CountAsync(predicate, ct)` | Counts entities matching the predicate (AsNoTracking). |
| `FirstOrDefaultAsync(predicate, orderBy, include, tracking, ct)` | First matching entity or null. |
| `Insert(entity)` | Marks an entity for insertion. Requires `SaveChangesAsync`. |
| `Insert(IEnumerable)` | Marks a collection for insertion. Requires `SaveChangesAsync`. |
| `InsertRangeAsync(entities, bulkConfig, ct)` | Bulk insert via EFCore.BulkExtensions. **Immediate write.** |
| `InsertOrUpdateAsync(entity, ct)` | Insert or update by PK. N+1 per entity. |
| `InsertOrUpdateAsync(IEnumerable, ct)` | Insert or update a collection by PK. N+1. |
| `InsertNewAsync(IEnumerable, ct)` | Inserts only new entities by PK. N+1. |
| `Update(entity)` | Marks an entity for update. Requires `SaveChangesAsync`. |
| `Update(IEnumerable)` | Marks a collection for update. Requires `SaveChangesAsync`. |
| `UpdateRangeAsync(where, setters, ct)` | Bulk update via `ExecuteUpdateAsync`. **Immediate write.** |
| `Delete(entity)` | Marks an entity for deletion. Requires `SaveChangesAsync`. |
| `Delete(IEnumerable)` | Marks a collection for deletion. Requires `SaveChangesAsync`. |
| `DeleteRangeAsync(where, ct)` | Bulk delete via `ExecuteDeleteAsync`. **Immediate write.** |
| `Attach(entity)` | Attaches an entity to the EF context (enables tracking). |
| `Detach(entity)` | Detaches an entity from the EF context. |

> **Immediate write** = the operation is sent directly to the database without calling `SaveChangesAsync`, bypassing the change tracker. EF Core interceptors are not invoked.

---

## IGenericUnitOfWork — API

| Method / property | Description |
|---|---|
| `Context` | Access to the underlying `BaseDbContext` (for advanced scenarios). |
| `Repository<TEntity>()` | Gets or creates a cached repository. Fail-fast when the entity is not in the model. |
| `SaveChangesAsync(ct)` | Saves tracked changes. Returns the number of entities written. |
| `BeginTransactionAsync(ct)` | Begins a transaction. Idempotent — a second call is ignored. |
| `CommitTransactionAsync(ct)` | Commits the active transaction. |
| `RollbackTransactionAsync(ct)` | Rolls back the active transaction. |
| `ExecuteSqlCommandAsync(query, ct)` | Executes a raw SQL command. Returns the number of affected rows. |
| `FromSql<TResult>(query)` | Composable `IQueryable` from raw SQL. |
| `ReloadAsync<TEntity>(entity, ct)` | Reloads an entity from the database, overwriting local changes. |
| `ExecuteResilientTransactionAsync(action, ct)` | Resilient transaction with automatic retry (ExecutionStrategy). |
| `ExecuteResilientTransactionAsync<T>(action, ct)` | Resilient transaction returning a result. |
| `ClearChangeTracker()` | Detaches all tracked entities. Required before a retry. |
| `Dispose()` / `DisposeAsync()` | Releases the DbContext and the active transaction. |

---

## Bulk operations

### InsertRangeAsync — EFCore.BulkExtensions

```csharp
var repo = uow.Repository<Product>();

var products = Enumerable.Range(1, 10_000)
    .Select(i => new Product { Id = i, Name = $"P{i}", Price = i * 1.5m, Category = "Bulk" });

await repo.InsertRangeAsync(products);
// No SaveChangesAsync needed — the data is already in the database
```

### UpdateRangeAsync — ExecuteUpdate (without the change tracker)

```csharp
// Deactivate all products in the "Sale" category — a single UPDATE in SQL
int updated = await repo.UpdateRangeAsync(
    p => p.Category == "Sale",
    s => s.SetProperty(p => p.IsActive, false)
          .SetProperty(p => p.Category, "Archive"));
```

> **EF Core 10:** The `UpdateRangeAsync` signature differs — the library uses `#if NET10_0_OR_GREATER` to handle both signatures (`Action<UpdateSettersBuilder<T>>` in EF10+, `Expression<Func<SetPropertyCalls<T>>>` in EF8/9).

### DeleteRangeAsync — ExecuteDelete (without the change tracker)

```csharp
// Delete all inactive products — a single DELETE in SQL
int deleted = await repo.DeleteRangeAsync(p => !p.IsActive);
```

---

## Resilient transactions

`ExecuteResilientTransactionAsync` automatically handles retries on transient failures (timeouts, deadlocks) via EF Core's `ExecutionStrategy`.

```csharp
// Requires a provider that supports retry, e.g. SQL Server with EnableRetryOnFailure
builder.Services.AddPersistence<AppDbContext, AppUnitOfWork>((sp, opt) =>
    opt.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));
```

```csharp
// Action executed atomically — automatic SaveChanges + Commit or Rollback
await uow.ExecuteResilientTransactionAsync(async (u, ct) =>
{
    var orders = u.Repository<Order>();
    var products = u.Repository<Product>();

    orders.Insert(new Order { CustomerName = "Kowalski", Total = 250m });
    products.Update(existingProduct);
});
```

Variant returning a result:

```csharp
var orderId = await uow.ExecuteResilientTransactionAsync(async (u, ct) =>
{
    var order = new Order { CustomerName = "Nowak", Total = 100m };
    u.Repository<Order>().Insert(order);
    return order.Id;
});
```

> **Important:** The action must be idempotent — it may be retried multiple times. The change tracker is cleared via `ClearChangeTracker()` before each attempt.

---

## Testing

### Unit tests — EF Core InMemory

```csharp
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString())
    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
    .Options;

using var ctx = new AppDbContext(options);
using var uow = new AppUnitOfWork(ctx);

// Repository<Order> works — OrderConfiguration is in the assembly
var repo = uow.Repository<Order>();
repo.Insert(new Order { Id = 1, CustomerName = "Test", Total = 100m });
await uow.SaveChangesAsync();

var found = await repo.FindAsync(1);
Assert.NotNull(found);
```

> **InMemory limitation:** `UpdateRangeAsync` and `DeleteRangeAsync` (ExecuteUpdate/Delete) require a relational provider. Use SQLite to test these operations.

### Integration tests — SQLite in-memory

```csharp
using var connection = new SqliteConnection("DataSource=:memory:");
connection.Open();

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(connection)
    .Options;

using var ctx = new AppDbContext(options);
ctx.Database.EnsureCreated(); // schema created from IEntityTypeConfiguration<T>
using var uow = new AppUnitOfWork(ctx);

// All bulk operations work with SQLite
await uow.Repository<Order>().DeleteRangeAsync(o => !o.IsActive);
```

---

## Service lifetimes

| Service | Default lifetime | Reason |
|---|---|---|
| `TDbContext` | Scoped | One DbContext per HTTP request — isolated change tracker |
| `TUnitOfWork` | Scoped | Shared UoW per request — consistent repository cache |
| `IGenericUnitOfWork` | Scoped | Alias for `TUnitOfWork` within the same scope |
| `IConnectionStringProvider<TDbContext>` | Scoped | Per-request connection string resolution for multi-tenancy |

`AddPersistence` and `AddUnitOfWork` accept optional `contextLifetime` and `optionsLifetime` parameters.

---

## Sample WebAPI

The `examples/TBJ.Persistence.EfCore.WebApiSample` project demonstrates:

- Full WebAPI with Swagger and multiple entities (Products, Orders)
- Multi-tenancy — separate database per tenant resolved via the `X-Tenant-Id` header
- DbContext without `DbSet<>` — entities registered exclusively through configurations
- Generic repositories available for every entity that has a configuration
- Resilient transactions — `ExecuteResilientTransactionAsync` used in order endpoints
- Bulk operations — `InsertRangeAsync`, `UpdateRangeAsync`, `DeleteRangeAsync`

```bash
cd examples/TBJ.Persistence.EfCore.WebApiSample
dotnet run
# Swagger UI available at: https://localhost:7001/swagger
```

Sample call with a tenant header:

```bash
curl -H "X-Tenant-Id: tenant-A" https://localhost:7001/api/products
curl -H "X-Tenant-Id: tenant-B" https://localhost:7001/api/products
# Each request hits a separate database
```

---

## Versioning and NuGet publishing

The project uses **MinVer** for automatic versioning via Git tags with the prefix `efcore/v` (e.g. `efcore/v1.0.0`).

```bash
git tag efcore/v1.0.0
git push origin efcore/v1.0.0
# GitHub Actions automatically publishes to NuGet.org and GitHub Packages
```

Pushing the tag triggers the `release.yml` GitHub Actions workflow, which:

- publishes the package to **NuGet.org** via Trusted Publishing (OIDC) — no API key stored as a secret,
- publishes the package to **GitHub Packages**.

The following secrets must be configured in the GitHub repository settings: `NUGET_USER` and `RELEASE_PAT`.

---

## License

[MIT](LICENSE)  
Author: [@tbudaj](https://github.com/tbudaj)
