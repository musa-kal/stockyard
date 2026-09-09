using Stockyard.Domain;
using Stockyard.Infrastructure.Persistence;
using Stockyard.Infrastructure.Procedures;

namespace Stockyard.IntegrationTests;

/// <summary>Creates master data with unique codes, so tests can share one database.</summary>
internal static class TestData
{
    public const string WebhookUrl = "http://fake-supplier.test/webhooks";

    public static string UniqueCode(string prefix) =>
        prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    public static async Task<Supplier> AddSupplierAsync(StockyardDbContext db, string? webhookUrl = WebhookUrl)
    {
        var supplier = Supplier.Create(UniqueCode("SUP"), "Northgate Pipe Supply", webhookUrl);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier;
    }

    public static async Task<Item> AddItemAsync(StockyardDbContext db, decimal unitCost = 12.5m)
    {
        var item = Item.Create(UniqueCode("ITEM-"), "Copper pipe 15 mm", "M", unitCost, 10);
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    public static async Task<JobSite> AddJobSiteAsync(StockyardDbContext db)
    {
        var site = JobSite.Create(UniqueCode("SITE"), "Riverside Clinic");
        db.JobSites.Add(site);
        await db.SaveChangesAsync();
        return site;
    }

    public static async Task<PurchaseOrder> AddDraftOrderAsync(
        StockyardDbContext db,
        Supplier supplier,
        Guid jobSiteId,
        params (Item Item, decimal Quantity)[] lines)
    {
        var order = new PurchaseOrder
        {
            Number = await db.NextPurchaseOrderNumberAsync(CancellationToken.None),
            SupplierId = supplier.Id,
            JobSiteId = jobSiteId,
        };
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync();

        foreach (var (item, quantity) in lines)
        {
            order.AddLine(item, quantity);
        }

        await db.SaveChangesAsync();
        return order;
    }

    /// <summary>Puts stock on hand the way production does: post an order and receive it.</summary>
    public static async Task<PurchaseOrder> ReceiveStockAsync(StockyardDbContext db, Item item, Guid jobSiteId, decimal quantity)
    {
        var supplier = await AddSupplierAsync(db, webhookUrl: null);
        var order = await AddDraftOrderAsync(db, supplier, jobSiteId, (item, quantity));
        var procedures = new StockyardProcedures(db);
        await procedures.PostPurchaseOrderAsync(order.Id, "seed-" + order.Number, CancellationToken.None);
        await procedures.ReceiveDeliveryAsync(order.Id, [new ReceiptLine(1, quantity)], CancellationToken.None);
        return order;
    }
}
