using Microsoft.EntityFrameworkCore;
using Stockyard.Domain;
using Stockyard.Infrastructure.Persistence;

namespace Stockyard.Exporter;

/// <summary>Reads one UTC day of posted order lines and ledger movements.</summary>
public sealed class ExportQueries(StockyardDbContext db)
{
    /// <summary>Lines of orders posted during the day.</summary>
    public async Task<List<OrderLineRecord>> OrderLinesAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var (from, to) = Range(day);
        var rows = await db.PurchaseOrderLines
            .AsNoTracking()
            .Join(db.PurchaseOrders, l => l.PurchaseOrderId, o => o.Id, (l, o) => new { Line = l, Order = o })
            .Where(x => x.Order.PostedAt >= from && x.Order.PostedAt < to)
            .OrderBy(x => x.Order.Number)
            .ThenBy(x => x.Line.LineNumber)
            .Select(x => new
            {
                x.Order.Id,
                x.Order.Number,
                SupplierCode = x.Order.Supplier!.Code,
                JobSiteCode = x.Order.JobSite!.Code,
                PostedAt = x.Order.PostedAt!.Value,
                x.Line.LineNumber,
                ItemSku = x.Line.Item!.Sku,
                x.Line.QuantityOrdered,
                x.Line.QuantityReceived,
                x.Line.UnitCost,
                x.Line.LineTotal,
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new OrderLineRecord
        {
            OrderId = r.Id.ToString(),
            OrderNumber = r.Number,
            SupplierCode = r.SupplierCode,
            JobSiteCode = r.JobSiteCode,
            PostedAt = r.PostedAt,
            LineNumber = r.LineNumber,
            ItemSku = r.ItemSku,
            QuantityOrdered = (double)r.QuantityOrdered,
            QuantityReceived = (double)r.QuantityReceived,
            UnitCost = (double)r.UnitCost,
            LineTotal = (double)r.LineTotal,
        })];
    }

    /// <summary>Ledger movements that occurred during the day.</summary>
    public async Task<List<StockMovementRecord>> MovementsAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var (from, to) = Range(day);
        var rows = await db.StockMovements
            .AsNoTracking()
            .Where(m => m.OccurredAt >= from && m.OccurredAt < to)
            .Join(db.Items, m => m.ItemId, i => i.Id, (m, i) => new { Movement = m, i.Sku })
            .GroupJoin(db.JobSites, x => x.Movement.JobSiteId, j => (Guid?)j.Id, (x, sites) => new { x.Movement, x.Sku, Sites = sites })
            .SelectMany(x => x.Sites.DefaultIfEmpty(), (x, site) => new
            {
                x.Movement.Id,
                x.Sku,
                JobSiteCode = site == null ? JobSite.WarehouseCode : site.Code,
                x.Movement.Kind,
                x.Movement.Quantity,
                x.Movement.Reference,
                x.Movement.OccurredAt,
            })
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new StockMovementRecord
        {
            MovementId = r.Id,
            ItemSku = r.Sku,
            JobSiteCode = r.JobSiteCode,
            Kind = r.Kind.ToString(),
            Quantity = (double)r.Quantity,
            Reference = r.Reference,
            OccurredAt = r.OccurredAt,
        })];
    }

    private static (DateTime From, DateTime To) Range(DateOnly day)
    {
        var from = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return (from, from.AddDays(1));
    }
}
