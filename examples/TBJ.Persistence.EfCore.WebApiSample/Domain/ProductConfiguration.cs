using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TBJ.Persistence.EfCore.WebApiSample.Domain;

/// <summary>
/// Konfiguracja EF Core dla encji <see cref="Product"/>.
/// Odkrywana automatycznie przez BaseDbContext.OnModelCreating — ApplyConfigurationsFromAssembly.
/// Nie jest potrzebna żadna właściwość DbSet&lt;Product&gt; w kontekście.
/// </summary>
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        // SQLite nie obsługuje decimal natively — przechowujemy jako TEXT
        builder.Property(p => p.Price).HasColumnType("TEXT");
        builder.Property(p => p.Category).HasMaxLength(100);
        builder.Property(p => p.Stock).IsRequired();
    }
}
