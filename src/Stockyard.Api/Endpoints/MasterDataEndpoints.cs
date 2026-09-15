using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Stockyard.Api.Contracts;
using Stockyard.Domain;
using Stockyard.Infrastructure.Persistence;

namespace Stockyard.Api.Endpoints;

/// <summary>Suppliers, items and job sites: create and read back by id.</summary>
public static class MasterDataEndpoints
{
    public static RouteGroupBuilder MapMasterDataEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var suppliers = api.MapGroup("/suppliers").WithTags("Suppliers");
        suppliers.MapPost("/", CreateSupplierAsync);
        suppliers.MapGet("/{id:guid}", GetSupplierAsync);

        return api;
    }

    private static async Task<Created<SupplierResponse>> CreateSupplierAsync(
        CreateSupplierRequest request,
        StockyardDbContext db,
        CancellationToken cancellationToken)
    {
        var supplier = Supplier.Create(request.Code, request.Name, request.WebhookUrl);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/v1/suppliers/{supplier.Id}", SupplierResponse.From(supplier));
    }

    private static async Task<Results<Ok<SupplierResponse>, NotFound>> GetSupplierAsync(
        Guid id,
        StockyardDbContext db,
        CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return supplier is null ? TypedResults.NotFound() : TypedResults.Ok(SupplierResponse.From(supplier));
    }
}
