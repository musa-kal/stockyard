using Stockyard.Domain;

namespace Stockyard.Api.Contracts;

public sealed record CreateSupplierRequest(string? Code, string? Name, string? WebhookUrl);

public sealed record SupplierResponse(Guid Id, string Code, string Name, string? WebhookUrl, DateTime CreatedAt)
{
    public static SupplierResponse From(Supplier supplier)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        return new(supplier.Id, supplier.Code, supplier.Name, supplier.WebhookUrl, supplier.CreatedAt);
    }
}
