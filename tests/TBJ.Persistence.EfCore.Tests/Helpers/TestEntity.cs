namespace TBJ.Persistence.EfCore.Tests.Helpers;

/// <summary>Simple entity used in unit tests.</summary>
public class TestEntity
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Arbitrary string value for assertion.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional numeric value for filter/sort tests.</summary>
    public int Value { get; set; }
}
