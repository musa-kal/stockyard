using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stockyard.Api.Contracts;
using Stockyard.Domain;
using Stockyard.Infrastructure.Persistence;
using Stockyard.Infrastructure.Procedures;

namespace Stockyard.Api.Endpoints;

public static class PurchaseOrderEndpoints
{
    public static RouteGroupBuilder MapPurchaseOrderEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var orders = api.MapGroup("/purchase-orders").WithTags("Purchase orders");
        orders.MapPost("/", CreateAsync);
        orders.MapGet("/{id:guid}", GetAsync);
        orders.MapPost("/{id:guid}/lines", AddLineAsync);
        orders.MapPost("/{id:guid}/post", PostAsync);

        return api;
    }

    private static async Task<Created<PurchaseOrderResponse>> CreateAsync(
        CreatePurchaseOrderRequest request,
        StockyardDbContext db,
        CancellationToken cancellationToken)
    {
        var supplierCode = Codes.NormalizeCode(request.SupplierCode, "supplierCode");
        var jobSiteCode = Codes.NormalizeCode(request.JobSiteCode, "jobSiteCode");

        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Code == supplierCode, cancellationToken)
            ?? throw new DomainException(ErrorNumbers.NotFound, $"Supplier {supplierCode} does not exist.");
        var site = await db.JobSites.SingleOrDefaultAsync(j => j.Code == jobSiteCode, cancellationToken)
            ?? throw new DomainException(ErrorNumbers.NotFound, $"Job site {jobSiteCode} does not exist.");
        if (!site.Active)
        {
            throw new DomainException($"Job site {jobSiteCode} is not active.");
        }

        var order = new PurchaseOrder
        {
            Number = await db.NextPurchaseOrderNumberAsync(cancellationToken),
            SupplierId = supplier.Id,
            JobSiteId = site.Id,
        };
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        var response = await PurchaseOrderReader.LoadAsync(db, order.Id, cancellationToken);
        return TypedResults.Created($"/api/v1/purchase-orders/{order.Id}", response);
    }

    private static async Task<Results<Ok<PurchaseOrderResponse>, NotFound>> GetAsync(
        Guid id,
        StockyardDbContext db,
        CancellationToken cancellationToken)
    {
        var order = await PurchaseOrderReader.LoadAsync(db, id, cancellationToken);
        return order is null ? TypedResults.NotFound() : TypedResults.Ok(order);
    }

    private static async Task<Created<CreatedPurchaseOrderLineResponse>> AddLineAsync(
        Guid id,
        AddPurchaseOrderLineRequest request,
        StockyardDbContext db,
        CancellationToken cancellationToken)
    {
        var sku = Codes.NormalizeSku(request.ItemSku, "itemSku");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The same lock usp_PostPurchaseOrder takes, so a line cannot slip in while the order is posted.
        var order = await db.PurchaseOrders
            .FromSql($"SELECT * FROM dbo.PurchaseOrder WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}")
            .Include(o => o.Lines)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorNumbers.NotFound, "Purchase order not found.");

        var item = await db.Items.SingleOrDefaultAsync(i => i.Sku == sku, cancellationToken)
            ?? throw new DomainException(ErrorNumbers.NotFound, $"Item {sku} does not exist.");

        var line = order.AddLine(item, request.QuantityOrdered, request.UnitCost);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/v1/purchase-orders/{order.Id}",
            new CreatedPurchaseOrderLineResponse(
                line.Id,
                order.Id,
                line.LineNumber,
                item.Sku,
                line.QuantityOrdered,
                line.QuantityReceived,
                line.UnitCost,
                line.LineTotal));
    }

    /// <summary>
    /// Posts a draft. The Idempotency-Key header makes a retried request safe: the same key
    /// returns the same order, a different key on a posted order is a 409.
    /// </summary>
    private static async Task<Ok<PurchaseOrderResponse>> PostAsync(
        Guid id,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        StockyardProcedures procedures,
        StockyardDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > IdempotencyKeyMaxLength)
        {
            throw new DomainException($"The {IdempotencyKeyHeader} header is required and must be 1 to {IdempotencyKeyMaxLength} characters.");
        }

        await procedures.PostPurchaseOrderAsync(id, idempotencyKey, cancellationToken);

        var order = await PurchaseOrderReader.LoadAsync(db, id, cancellationToken);
        return TypedResults.Ok(order);
    }

    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private const int IdempotencyKeyMaxLength = 64;
}
