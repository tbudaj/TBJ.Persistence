namespace TBJ.Persistence.EfCore.WebApiSample.Domain;

/// <summary>Encja produktu w katalogu.</summary>
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = string.Empty;
    public int Stock { get; set; }
    public bool IsActive { get; set; } = true;
}
