using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TBJ.Persistence.EfCore.WebApiSample.Domain;

/// <summary>
/// Konfiguracja EF Core dla encji <see cref="OrderItem"/>.
/// Odkrywana automatycznie — nie potrzeba DbSet&lt;OrderItem&gt; w kontekście.
/// </summary>
public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.HasKey(i => i.Id);
        // SQLite nie obsługuje decimal natively — przechowujemy jako TEXT
        builder.Property(i => i.UnitPrice).HasColumnType("TEXT");
        builder.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId);
    }
}
