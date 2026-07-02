namespace TBJ.Persistence.EfCore.IntegrationTests.Helpers;

/// <summary>
/// Sample domain entity used in integration tests.
/// Represents a simple product with name, price and category.
/// </summary>
public class Product
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Product name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Unit price (must be non-negative).</summary>
    public decimal Price { get; set; }

    /// <summary>Product category for filter tests.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Indicates whether the product is active.</summary>
    public bool IsActive { get; set; } = true;
}
