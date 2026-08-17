namespace Stockyard.Domain;

public sealed class Item
{
    public Guid Id { get; set; }

    public required string Sku { get; set; }

    public required string Description { get; set; }

    public required string UnitOfMeasure { get; set; }

    public decimal UnitCost { get; set; }

    public decimal ReorderPoint { get; set; }

    public DateTime CreatedAt { get; set; }
}
