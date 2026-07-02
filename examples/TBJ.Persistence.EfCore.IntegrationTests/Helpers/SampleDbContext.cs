using Microsoft.EntityFrameworkCore;
using TBJ.Persistence.EfCore.Implementation;

namespace TBJ.Persistence.EfCore.IntegrationTests.Helpers;

/// <summary>
/// Sample DbContext for integration tests.
/// Inherits from <see cref="BaseDbContext"/> — entity configurations are auto-discovered
/// from this assembly via <c>ApplyConfigurationsFromAssembly</c>.
/// </summary>
public class SampleDbContext : BaseDbContext
{
    /// <summary>Products entity set.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>Initializes the context with the supplied options.</summary>
    public SampleDbContext(DbContextOptions<SampleDbContext> options) : base(options) { }
}
