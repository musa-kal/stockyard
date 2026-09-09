using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stockyard.Domain;
using Stockyard.Domain.Events;
using Stockyard.Infrastructure.Persistence;
using Stockyard.Infrastructure.Procedures;
using Stockyard.Infrastructure.Queries;

namespace Stockyard.IntegrationTests.Infrastructure;

public sealed class ReceiveDeliveryTests(SqlServerFixture sql)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PartialThenFullReceiptMovesTheOrderThroughItsStatuses()
    {
        await using var db = sql.CreateDbContext();
        var (order, site, valve, pipe) = await PostedOrderAsync(db);
        var procedures = new StockyardProcedures(db);

        await procedures.ReceiveDeliveryAsync(order.Id, [new ReceiptLine(1, 6)], Ct);
        (await StatusAsync(db, order.Id)).ShouldBe(PurchaseOrderStatus.PartiallyReceived);

        await procedures.ReceiveDeliveryAsync(order.Id, [new ReceiptLine(1, 4), new ReceiptLine(2, 25)], Ct);
        (await StatusAsync(db, order.Id)).ShouldBe(PurchaseOrderStatus.Received);

        var movements = await db.StockMovements.Where(m => m.Reference == order.Number).OrderBy(m => m.Id).ToListAsync(Ct);
        movements.Select(m => (m.ItemId, m.Quantity)).ShouldBe([(valve.Id, 6m), (valve.Id, 4m), (pipe.Id, 25m)]);
        movements.ShouldAllBe(m => m.Kind == MovementKind.Receipt && m.JobSiteId == site.Id);

        var onHand = await new StockQueries(db).GetOnHandAsync(valve.Sku, null, Ct);
        onHand.Single().Quantity.ShouldBe(10m);
        onHand.Single().JobSiteCode.ShouldBe(site.Code);
    }

    [Fact]
    public async Task OverReceiptIsRejectedAndChangesNothing()
    {
        await using var db = sql.CreateDbContext();
        var (order, _, _, _) = await PostedOrderAsync(db);

        var ex = await Should.ThrowAsync<DomainException>(
            () => new StockyardProcedures(db).ReceiveDeliveryAsync(order.Id, [new ReceiptLine(2, 5), new ReceiptLine(1, 11)], Ct));

        ex.ErrorNumber.ShouldBe(ErrorNumbers.OverReceipt);
        (await db.PurchaseOrderLines.Where(l => l.PurchaseOrderId == order.Id).SumAsync(l => l.QuantityReceived, Ct)).ShouldBe(0m);
        (await db.StockMovements.CountAsync(m => m.Reference == order.Number, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task RepeatedLineNumbersAreSummedBeforeTheCheck()
    {
        await using var db = sql.CreateDbContext();
        var (order, _, _, _) = await PostedOrderAsync(db);

        var ex = await Should.ThrowAsync<DomainException>(
            () => new StockyardProcedures(db).ReceiveDeliveryAsync(order.Id, [new ReceiptLine(1, 6), new ReceiptLine(1, 6)], Ct));

        ex.ErrorNumber.ShouldBe(ErrorNumbers.OverReceipt);
    }

    [Fact]
    public async Task ReceivingADraftIsAnInvalidState()
    {
        await using var db = sql.CreateDbContext();
        var supplier = await TestData.AddSupplierAsync(db);
        var item = await TestData.AddItemAsync(db);
        var order = await TestData.AddDraftOrderAsync(db, supplier, JobSite.WarehouseId, (item, 3));

        var ex = await Should.ThrowAsync<DomainException>(
            () => new StockyardProcedures(db).ReceiveDeliveryAsync(order.Id, [new ReceiptLine(1, 1)], Ct));

        ex.ErrorNumber.ShouldBe(ErrorNumbers.InvalidState);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -2)]
    public async Task UnknownLinesAndNonPositiveQuantitiesAreInvalid(int lineNumber, int quantity)
    {
        await using var db = sql.CreateDbContext();
        var (order, _, _, _) = await PostedOrderAsync(db);

        var ex = await Should.ThrowAsync<DomainException>(
            () => new StockyardProcedures(db).ReceiveDeliveryAsync(order.Id, [new ReceiptLine(lineNumber, quantity)], Ct));

        ex.ErrorNumber.ShouldBe(ErrorNumbers.Validation);
    }

    [Fact]
    public async Task ReceivingIntoTheWarehouseStoresANullSite()
    {
        await using var db = sql.CreateDbContext();
        var item = await TestData.AddItemAsync(db);

        var order = await TestData.ReceiveStockAsync(db, item, JobSite.WarehouseId, 8);

        var movement = await db.StockMovements.SingleAsync(m => m.Reference == order.Number, Ct);
        movement.JobSiteId.ShouldBeNull();
        var onHand = (await new StockQueries(db).GetOnHandAsync(item.Sku, JobSite.WarehouseCode, Ct)).Single();
        onHand.Quantity.ShouldBe(8m);
        onHand.LastMovementAt.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public async Task ReceiptWritesTheDeliveryReceivedEventAndWebhook()
    {
        await using var db = sql.CreateDbContext();
        var (order, site, valve, _) = await PostedOrderAsync(db);

        await new StockyardProcedures(db).ReceiveDeliveryAsync(order.Id, [new ReceiptLine(1, 2.5m)], Ct);

        var id = order.Id.ToString().ToUpperInvariant();
        var row = await db.OutboxMessages.SingleAsync(o => o.Type == EventTypes.DeliveryReceived && o.Payload.Contains(id), Ct);
        var payload = JsonSerializer.Deserialize<DeliveryReceived>(row.Payload, Json)!;
        payload.OrderId.ShouldBe(order.Id);
        payload.JobSiteCode.ShouldBe(site.Code);
        payload.Lines.ShouldBe([new DeliveryReceivedLine(1, valve.Sku, 2.5m)]);
        (await db.WebhookDeliveries.CountAsync(w => w.SupplierId == order.SupplierId && w.EventType == EventTypes.DeliveryReceived, Ct)).ShouldBe(1);
    }

    private static async Task<(PurchaseOrder Order, JobSite Site, Item Valve, Item Pipe)> PostedOrderAsync(StockyardDbContext db)
    {
        var supplier = await TestData.AddSupplierAsync(db);
        var site = await TestData.AddJobSiteAsync(db);
        var valve = await TestData.AddItemAsync(db, 18.75m);
        var pipe = await TestData.AddItemAsync(db, 4.1m);
        var order = await TestData.AddDraftOrderAsync(db, supplier, site.Id, (valve, 10), (pipe, 25));
        await new StockyardProcedures(db).PostPurchaseOrderAsync(order.Id, "post-" + order.Number, Ct);
        return (order, site, valve, pipe);
    }

    private static Task<PurchaseOrderStatus> StatusAsync(StockyardDbContext db, Guid orderId) =>
        db.PurchaseOrders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync(Ct);
}
