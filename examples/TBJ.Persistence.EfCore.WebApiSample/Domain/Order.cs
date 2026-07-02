namespace TBJ.Persistence.EfCore.WebApiSample.Domain;

/// <summary>Encja zamówienia.</summary>
public class Order
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsConfirmed { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
