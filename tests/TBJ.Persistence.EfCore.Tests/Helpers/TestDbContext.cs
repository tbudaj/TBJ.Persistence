using Microsoft.EntityFrameworkCore;
using TBJ.Persistence.EfCore.Implementation;

namespace TBJ.Persistence.EfCore.Tests.Helpers;

/// <summary>
/// Minimal DbContext used in unit tests.
/// Configured with the EF Core InMemory provider — no database connection required.
/// </summary>
public class TestDbContext : BaseDbContext
{
    /// <summary>Entity set used by tests.</summary>
    public DbSet<TestEntity> TestEntities => Set<TestEntity>();

    /// <summary>Initializes the context with the supplied options.</summary>
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<TestEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
        });
    }
}
