namespace TBJ.Persistence.EfCore.WebApiSample.Domain;

/// <summary>Pozycja zamówienia — powiązana z Order kluczem obcym.</summary>
public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public Order Order { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
