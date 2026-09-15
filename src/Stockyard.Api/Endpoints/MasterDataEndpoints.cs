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

        var items = api.MapGroup("/items").WithTags("Items");
        items.MapPost("/", CreateItemAsync);
        items.MapGet("/{id:guid}", GetItemAsync);

        var jobSites = api.MapGroup("/job-sites").WithTags("Job sites");
        jobSites.MapPost("/", CreateJobSiteAsync);
        jobSites.MapGet("/{id:guid}", GetJobSiteAsync);

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

    private static async Task<Created<ItemResponse>> CreateItemAsync(
        CreateItemRequest request,
        StockyardDbContext db,
        CancellationToken cancellationToken)
    {
        var item = Item.Create(request.Sku, request.Description, request.UnitOfMeasure, request.UnitCost, request.ReorderPoint);
        db.Items.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/v1/items/{item.Id}", ItemResponse.From(item));
    }

    private static async Task<Results<Ok<ItemResponse>, NotFound>> GetItemAsync(
        Guid id,
        StockyardDbContext db,
        CancellationToken cancellationToken)
    {
        var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        return item is null ? TypedResults.NotFound() : TypedResults.Ok(ItemResponse.From(item));
    }

    private static async Task<Created<JobSiteResponse>> CreateJobSiteAsync(
        CreateJobSiteRequest request,
        StockyardDbContext db,
        CancellationToken cancellationToken)
    {
        var site = JobSite.Create(request.Code, request.Name);
        db.JobSites.Add(site);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/v1/job-sites/{site.Id}", JobSiteResponse.From(site));
    }

    private static async Task<Results<Ok<JobSiteResponse>, NotFound>> GetJobSiteAsync(
        Guid id,
        StockyardDbContext db,
        CancellationToken cancellationToken)
    {
        var site = await db.JobSites.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, cancellationToken);
        return site is null ? TypedResults.NotFound() : TypedResults.Ok(JobSiteResponse.From(site));
    }
}
