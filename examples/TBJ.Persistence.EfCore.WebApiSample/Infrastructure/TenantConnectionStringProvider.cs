using Microsoft.Extensions.Caching.Memory;
using TBJ.Persistence.EfCore.Abstractions;

namespace TBJ.Persistence.EfCore.WebApiSample.Infrastructure;

/// <summary>
/// Dostawca connection stringa per-request na podstawie nagłówka HTTP X-Tenant-Id.
/// Wywoływany przez AddPersistence podczas budowania opcji DbContext (raz per Scoped lifetime).
/// Connection stringi są cachowane w IMemoryCache przez 5 minut dla wydajności.
/// </summary>
public class TenantConnectionStringProvider : IConnectionStringProvider<AppDbContext>
{
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly ITenantStore tenantStore;
    private readonly IMemoryCache cache;
    private readonly ILogger<TenantConnectionStringProvider> logger;

    /// <summary>Inicjalizuje dostawcę z wymaganymi zależnościami.</summary>
    public TenantConnectionStringProvider(
        IHttpContextAccessor httpContextAccessor,
        ITenantStore tenantStore,
        IMemoryCache cache,
        ILogger<TenantConnectionStringProvider> logger)
    {
        this.httpContextAccessor = httpContextAccessor;
        this.tenantStore = tenantStore;
        this.cache = cache;
        this.logger = logger;
    }

    /// <summary>
    /// Zwraca connection string dla tenanta z nagłówka X-Tenant-Id.
    /// Wyrzuca InvalidOperationException gdy nagłówek jest pusty lub tenant nie istnieje.
    /// </summary>
    public string GetConnectionString()
    {
        var tenantId = httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].ToString();

        if (string.IsNullOrEmpty(tenantId))
            throw new InvalidOperationException("Brak nagłówka X-Tenant-Id w żądaniu HTTP.");

        var cacheKey = $"cs:{tenantId}";
        if (!cache.TryGetValue(cacheKey, out string? connectionString))
        {
            connectionString = tenantStore.GetConnectionString(tenantId);
            if (connectionString is null)
                throw new InvalidOperationException($"Tenant '{tenantId}' nie jest zarejestrowany.");

            cache.Set(cacheKey, connectionString, TimeSpan.FromMinutes(5));
            logger.LogDebug("Connection string dla tenanta '{TenantId}' pobrany ze sklepu i zcachowany.", tenantId);
        }
        else
        {
            logger.LogDebug("Connection string dla tenanta '{TenantId}' pobrany z cache.", tenantId);
        }

        logger.LogInformation("Resolwowanie DbContext dla tenanta '{TenantId}'.", tenantId);
        return connectionString!;
    }
}
