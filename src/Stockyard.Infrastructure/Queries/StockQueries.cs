using Microsoft.EntityFrameworkCore;
using Stockyard.Infrastructure.Persistence;

namespace Stockyard.Infrastructure.Queries;

/// <summary>Read paths over the hand-written views.</summary>
public sealed class StockQueries(StockyardDbContext db)
{
    public async Task<IReadOnlyList<StockOnHandRow>> GetOnHandAsync(string? itemSku, string? jobSiteCode, CancellationToken cancellationToken)
    {
        var query = db.Database.SqlQuery<StockOnHandRow>(
            $"SELECT ItemSku, JobSiteCode, Quantity, LastMovementAt FROM dbo.vw_StockOnHand");

        if (itemSku is not null)
        {
            query = query.Where(r => r.ItemSku == itemSku);
        }

        if (jobSiteCode is not null)
        {
            query = query.Where(r => r.JobSiteCode == jobSiteCode);
        }

        var rows = await query
            .OrderBy(r => r.ItemSku)
            .ThenBy(r => r.JobSiteCode)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => r with { LastMovementAt = AsUtc(r.LastMovementAt) })];
    }

    public async Task<IReadOnlyList<OpenPurchaseOrderLineRow>> GetOpenPurchaseOrderLinesAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Database
            .SqlQuery<OpenPurchaseOrderLineRow>(
                $"""
                SELECT PurchaseOrderId, Number, Status, SupplierCode, JobSiteCode, PostedAt, LineNumber,
                       ItemSku, QuantityOrdered, QuantityReceived, QuantityOutstanding, UnitCost
                FROM dbo.vw_OpenPurchaseOrders
                """)
            .OrderBy(r => r.PostedAt)
            .ThenBy(r => r.Number)
            .ThenBy(r => r.LineNumber)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => r with { PostedAt = r.PostedAt is { } postedAt ? AsUtc(postedAt) : null })];
    }

    // Ad hoc SqlQuery types are not part of the model, so the UTC converter does not reach them.
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
