namespace Stockyard.Domain;

public sealed class Item
{
    public const int DescriptionMaxLength = 200;

    public const int UnitOfMeasureMaxLength = 8;

    public Guid Id { get; set; }

    public required string Sku { get; set; }

    public required string Description { get; set; }

    public required string UnitOfMeasure { get; set; }

    public decimal UnitCost { get; set; }

    public decimal ReorderPoint { get; set; }

    public DateTime CreatedAt { get; set; }

    public static Item Create(string? sku, string? description, string? unitOfMeasure, decimal unitCost, decimal reorderPoint)
    {
        if (unitCost < 0)
        {
            throw new DomainException("unitCost cannot be negative.");
        }

        if (reorderPoint < 0)
        {
            throw new DomainException("reorderPoint cannot be negative.");
        }

        return new Item
        {
            Sku = Codes.NormalizeSku(sku, "sku"),
            Description = Text.Required(description, DescriptionMaxLength, "description"),
            UnitOfMeasure = Codes.Normalize(unitOfMeasure, UnitOfMeasureMaxLength, "unitOfMeasure"),
            UnitCost = unitCost,
            ReorderPoint = reorderPoint,
        };
    }
}
