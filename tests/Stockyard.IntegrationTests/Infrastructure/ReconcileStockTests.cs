using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stockyard.Domain;
using Stockyard.Domain.Events;
using Stockyard.Infrastructure.Persistence;
using Stockyard.Infrastructure.Procedures;

namespace Stockyard.IntegrationTests.Infrastructure;

public sealed class ReconcileStockTests(SqlServerFixture sql)
{
    private const string ProjectionsConsumer = "projections";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReportOnlyFindsDriftWithoutChangingIt()
    {
        await using var db = sql.CreateDbContext();
        var (item, site) = await ProjectedStockAsync(db, received: 10, projected: 7);

        var differences = await new StockyardProcedures(db).ReconcileStockAsync(apply: false, Ct);

        differences.ShouldContain(new StockDifference(item.Sku, site.Code, 7m, 10m));
        (await ProjectedAsync(db, item.Id, site.Id)).ShouldBe(7m);
    }

    [Fact]
    public async Task ApplyCorrectsTheProjectionAndWritesAnEvent()
    {
        await using var db = sql.CreateDbContext();
        var (item, site) = await ProjectedStockAsync(db, received: 10, projected: 7);

        var differences = await new StockyardProcedures(db).ReconcileStockAsync(apply: true, Ct);

        differences.ShouldContain(new StockDifference(item.Sku, site.Code, 7m, 10m));
        (await ProjectedAsync(db, item.Id, site.Id)).ShouldBe(10m);

        var row = await db.OutboxMessages.SingleAsync(o => o.Type == EventTypes.StockReconciled && o.Payload.Contains(item.Sku), Ct);
        var payload = JsonSerializer.Deserialize<StockReconciled>(row.Payload, JsonSerializerOptions.Web)!;
        payload.Differences.ShouldContain(new StockDifference(item.Sku, site.Code, 7m, 10m));

        (await new StockyardProcedures(db).ReconcileStockAsync(apply: false, Ct))
            .ShouldNotContain(d => d.ItemSku == item.Sku);
    }

    [Fact]
    public async Task ApplyCreatesMissingProjectionRowsAndZeroesStaleOnes()
    {
        await using var db = sql.CreateDbContext();
        var item = await TestData.AddItemAsync(db);
        var site = await TestData.AddJobSiteAsync(db);
        var order = await TestData.ReceiveStockAsync(db, item, JobSite.WarehouseId, 4);
        await MarkProjectedAsync(db, order.Id);
        db.StockOnHand.Add(new StockOnHand { ItemId = item.Id, JobSiteId = site.Id, Quantity = 9, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(Ct);

        await new StockyardProcedures(db).ReconcileStockAsync(apply: true, Ct);

        (await ProjectedAsync(db, item.Id, JobSite.WarehouseId)).ShouldBe(4m);
        (await ProjectedAsync(db, item.Id, site.Id)).ShouldBe(0m);
    }

    [Fact]
    public async Task PairsWithUnappliedEventsAreLeftAlone()
    {
        await using var db = sql.CreateDbContext();
        var item = await TestData.AddItemAsync(db);
        var site = await TestData.AddJobSiteAsync(db);

        // Received, but the projections worker has not applied the DeliveryReceived event yet.
        await TestData.ReceiveStockAsync(db, item, site.Id, 6);

        var differences = await new StockyardProcedures(db).ReconcileStockAsync(apply: true, Ct);

        differences.ShouldNotContain(d => d.ItemSku == item.Sku);
        (await db.StockOnHand.AnyAsync(s => s.ItemId == item.Id, Ct)).ShouldBeFalse();
    }

    private static async Task<(Item Item, JobSite Site)> ProjectedStockAsync(StockyardDbContext db, decimal received, decimal projected)
    {
        var item = await TestData.AddItemAsync(db);
        var site = await TestData.AddJobSiteAsync(db);
        var order = await TestData.ReceiveStockAsync(db, item, site.Id, received);
        await MarkProjectedAsync(db, order.Id);
        db.StockOnHand.Add(new StockOnHand { ItemId = item.Id, JobSiteId = site.Id, Quantity = projected, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(Ct);
        return (item, site);
    }

    /// <summary>Records the guard row the projections worker writes when it applies the receipt.</summary>
    private static async Task MarkProjectedAsync(StockyardDbContext db, Guid orderId)
    {
        var id = orderId.ToString().ToUpperInvariant();
        var messageId = await db.OutboxMessages
            .Where(o => o.Type == EventTypes.DeliveryReceived && o.Payload.Contains(id))
            .Select(o => o.Id)
            .SingleAsync(Ct);
        db.ProcessedMessages.Add(new ProcessedMessage { ConsumerName = ProjectionsConsumer, MessageId = messageId });
        await db.SaveChangesAsync(Ct);
    }

    private static Task<decimal> ProjectedAsync(StockyardDbContext db, Guid itemId, Guid siteId) =>
        db.StockOnHand
            .Where(s => s.ItemId == itemId && s.JobSiteId == siteId)
            .Select(s => s.Quantity)
            .SingleAsync(Ct);
}
