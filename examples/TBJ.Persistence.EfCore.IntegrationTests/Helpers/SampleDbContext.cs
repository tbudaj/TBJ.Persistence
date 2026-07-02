using Microsoft.EntityFrameworkCore;
using TBJ.Persistence.EfCore.Implementation;

namespace TBJ.Persistence.EfCore.IntegrationTests.Helpers;

/// <summary>
/// Przykładowy DbContext dla testów integracyjnych.
/// Dziedziczy po <see cref="BaseDbContext"/> — żadnych właściwości <c>DbSet&lt;T&gt;</c>.
/// Encja <see cref="Product"/> jest rejestrowana w modelu automatycznie przez
/// <see cref="ProductConfiguration"/> odkrywaną przez <c>ApplyConfigurationsFromAssembly</c>.
/// Dostęp do encji odbywa się przez <c>uow.Repository&lt;Product&gt;()</c>.
/// </summary>
public class SampleDbContext : BaseDbContext
{
    /// <summary>Inicjalizuje kontekst z podanymi opcjami EF Core.</summary>
    public SampleDbContext(DbContextOptions<SampleDbContext> options) : base(options) { }
}
