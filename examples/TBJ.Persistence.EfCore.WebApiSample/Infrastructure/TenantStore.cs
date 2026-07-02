namespace TBJ.Persistence.EfCore.WebApiSample.Infrastructure;

/// <summary>Kontrakt przechowywania konfiguracji tenantów.</summary>
public interface ITenantStore
{
    /// <summary>Zwraca connection string dla podanego tenanta lub null gdy tenant nie istnieje.</summary>
    string? GetConnectionString(string tenantId);

    /// <summary>Zwraca listę wszystkich zarejestrowanych identyfikatorów tenantów.</summary>
    IReadOnlyList<string> GetAllTenantIds();
}

/// <summary>
/// Implementacja in-memory rejestrująca predefiniowanych tenantów ze wskazaniem na pliki SQLite.
/// W produkcji zastąp implementacją czytającą z bazy danych lub konfiguracji.
/// Tenant "tenant-a" — Data Source=tenant-a.db
/// Tenant "tenant-b" — Data Source=tenant-b.db
/// Tenant "tenant-demo" — Data Source=:memory: (SQLite in-memory dla demonstracji)
/// </summary>
public class InMemoryTenantStore : ITenantStore
{
    private static readonly Dictionary<string, string> Tenants = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tenant-a"]    = "Data Source=tenant-a.db",
        ["tenant-b"]    = "Data Source=tenant-b.db",
        ["tenant-demo"] = "Data Source=:memory:",
    };

    /// <inheritdoc/>
    public string? GetConnectionString(string tenantId)
        => Tenants.TryGetValue(tenantId, out var cs) ? cs : null;

    /// <inheritdoc/>
    public IReadOnlyList<string> GetAllTenantIds() => Tenants.Keys.ToList();
}
