using Microsoft.EntityFrameworkCore;

namespace TBJ.Persistence.EfCore.Abstractions;

/// <summary>
/// Bazowy kontrakt dla dostawców connection stringów.
/// Implementuj ten interfejs, aby dostarczać connection stringi dynamicznie
/// (np. per-request dla multi-tenancy).
/// </summary>
public interface IConnectionStringProvider
{
    /// <summary>Zwraca connection string dla bieżącego kontekstu wykonania.</summary>
    string GetConnectionString();
}

/// <summary>
/// Typowany dostawca connection stringa dla konkretnego <see cref="DbContext"/>.
/// Umożliwia resolwowanie różnych connection stringów per typ kontekstu z kontenera DI.
/// </summary>
/// <typeparam name="TDbContext">Typ DbContext, dla którego dostarczany jest connection string.</typeparam>
public interface IConnectionStringProvider<out TDbContext> : IConnectionStringProvider
    where TDbContext : DbContext
{
}
