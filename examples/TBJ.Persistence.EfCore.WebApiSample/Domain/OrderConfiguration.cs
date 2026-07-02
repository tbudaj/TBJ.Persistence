using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TBJ.Persistence.EfCore.WebApiSample.Domain;

/// <summary>
/// Konfiguracja EF Core dla encji <see cref="Order"/>.
/// Odkrywana automatycznie — nie potrzeba DbSet&lt;Order&gt; w kontekście.
/// </summary>
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.CustomerName).IsRequired().HasMaxLength(200);
        builder.Property(o => o.CustomerEmail).HasMaxLength(200);
        builder.Property(o => o.CreatedAt).IsRequired();
        builder.HasMany(o => o.Items).WithOne(i => i.Order).HasForeignKey(i => i.OrderId);
    }
}
