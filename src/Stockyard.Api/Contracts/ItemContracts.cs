using Stockyard.Domain;

namespace Stockyard.Api.Contracts;

public sealed record CreateItemRequest(string? Sku, string? Description, string? UnitOfMeasure, decimal UnitCost, decimal ReorderPoint);

public sealed record ItemResponse(Guid Id, string Sku, string Description, string UnitOfMeasure, decimal UnitCost, decimal ReorderPoint)
{
    public static ItemResponse From(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new(item.Id, item.Sku, item.Description, item.UnitOfMeasure, item.UnitCost, item.ReorderPoint);
    }
}
