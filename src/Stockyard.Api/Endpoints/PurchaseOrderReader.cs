using Microsoft.EntityFrameworkCore;
using Stockyard.Api.Contracts;
using Stockyard.Infrastructure.Persistence;

namespace Stockyard.Api.Endpoints;

/// <summary>Projects purchase orders straight into the response shape in one query.</summary>
internal static class PurchaseOrderReader
{
    public static IQueryable<PurchaseOrderResponse> Query(StockyardDbContext db) =>
        db.PurchaseOrders
            .AsNoTracking()
            .Select(o => new PurchaseOrderResponse(
                o.Id,
                o.Number,
                o.Status,
                o.Supplier!.Code,
                o.JobSite!.Code,
                o.PostedAt,
                o.Lines
                    .OrderBy(l => l.LineNumber)
                    .Select(l => new PurchaseOrderLineResponse(
                        l.LineNumber,
                        l.Item!.Sku,
                        l.QuantityOrdered,
                        l.QuantityReceived,
                        l.UnitCost,
                        l.LineTotal))
                    .ToList()));

    public static Task<PurchaseOrderResponse?> LoadAsync(StockyardDbContext db, Guid id, CancellationToken cancellationToken) =>
        Query(db).SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
}
