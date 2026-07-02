using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TBJ.Persistence.EfCore.IntegrationTests.Helpers;

/// <summary>
/// EF Core fluent configuration for <see cref="Product"/>.
/// Demonstrates how <see cref="TBJ.Persistence.EfCore.Implementation.BaseDbContext.OnModelCreating"/> 
/// auto-discovers <see cref="IEntityTypeConfiguration{TEntity}"/> implementations from the assembly.
/// </summary>
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Price).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Category).HasMaxLength(100);
    }
}
