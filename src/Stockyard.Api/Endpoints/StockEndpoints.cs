using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Stockyard.Api.Contracts;
using Stockyard.Domain;
using Stockyard.Infrastructure.Persistence;
using Stockyard.Infrastructure.Procedures;
using Stockyard.Infrastructure.Queries;

namespace Stockyard.Api.Endpoints;

public static class StockEndpoints
{
    private const int ReferenceMaxLength = 40;

    public static RouteGroupBuilder MapStockEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var stock = api.MapGroup("/stock").WithTags("Stock");
        stock.MapPost("/issues", IssueAsync);
        stock.MapGet("/on-hand", GetOnHandAsync);

        return api;
    }

    private static async Task<Created<StockIssueResponse>> IssueAsync(
        IssueStockRequest request,
        StockyardDbContext db,
        StockyardProcedures procedures,
        CancellationToken cancellationToken)
    {
        var sku = Codes.NormalizeSku(request.ItemSku, "itemSku");
        var siteCode = Codes.NormalizeCode(request.JobSiteCode, "jobSiteCode");
        var reference = Text.Required(request.Reference, ReferenceMaxLength, "reference");
        if (request.Quantity <= 0)
        {
            throw new DomainException("quantity must be greater than zero.");
        }

        var itemId = await db.Items.Where(i => i.Sku == sku).Select(i => (Guid?)i.Id).SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorNumbers.NotFound, $"Item {sku} does not exist.");
        var siteId = await db.JobSites.Where(j => j.Code == siteCode).Select(j => (Guid?)j.Id).SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorNumbers.NotFound, $"Job site {siteCode} does not exist.");

        var issued = await procedures.IssueStockAsync(itemId, siteId, request.Quantity, reference, cancellationToken);

        return TypedResults.Created(
            $"/api/v1/stock/on-hand?itemSku={Uri.EscapeDataString(sku)}&jobSiteCode={Uri.EscapeDataString(siteCode)}",
            new StockIssueResponse(issued.MovementId, sku, siteCode, request.Quantity, reference, issued.OccurredAt));
    }

    /// <summary>Reads dbo.vw_StockOnHand, the ledger itself, not the projection.</summary>
    private static async Task<Ok<List<StockOnHandResponse>>> GetOnHandAsync(
        string? itemSku,
        string? jobSiteCode,
        StockQueries queries,
        CancellationToken cancellationToken)
    {
        var sku = string.IsNullOrWhiteSpace(itemSku) ? null : Codes.NormalizeSku(itemSku, "itemSku");
        var site = string.IsNullOrWhiteSpace(jobSiteCode) ? null : Codes.NormalizeCode(jobSiteCode, "jobSiteCode");

        var rows = await queries.GetOnHandAsync(sku, site, cancellationToken);
        return TypedResults.Ok(rows
            .Select(r => new StockOnHandResponse(r.ItemSku, r.JobSiteCode, r.Quantity, r.LastMovementAt))
            .ToList());
    }
}
