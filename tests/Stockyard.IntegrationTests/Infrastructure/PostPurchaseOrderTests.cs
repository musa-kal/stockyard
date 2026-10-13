using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stockyard.Domain;
using Stockyard.Domain.Events;
using Stockyard.Infrastructure.Procedures;

namespace Stockyard.IntegrationTests.Infrastructure;

public sealed class PostPurchaseOrderTests(SqlServerFixture sql)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task PostingTwiceWithTheSameKeyPostsOnce()
    {
        await using var db = sql.CreateDbContext();
        var (order, _) = await DraftOrderAsync(db);
        var procedures = new StockyardProcedures(db);

        await procedures.PostPurchaseOrderAsync(order.Id, "key-" + order.Number, TestContext.Current.CancellationToken);
        await procedures.PostPurchaseOrderAsync(order.Id, "key-" + order.Number, TestContext.Current.CancellationToken);

        var posted = await db.PurchaseOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id, TestContext.Current.CancellationToken);
        posted.Status.ShouldBe(PurchaseOrderStatus.Posted);
        posted.PostedAt.ShouldNotBeNull();
        posted.PostedAt.Value.Kind.ShouldBe(DateTimeKind.Utc);
        (await OutboxRowsForAsync(db, order.Id)).Count.ShouldBe(1);
        (await db.WebhookDeliveries.CountAsync(w => w.SupplierId == order.SupplierId, TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task PostingWithADifferentKeyIsAConflict()
    {
        await using var db = sql.CreateDbContext();
        var (order, _) = await DraftOrderAsync(db);
        var procedures = new StockyardProcedures(db);
        await procedures.PostPurchaseOrderAsync(order.Id, "first-" + order.Number, TestContext.Current.CancellationToken);

        var ex = await Should.ThrowAsync<DomainException>(
            () => procedures.PostPurchaseOrderAsync(order.Id, "second-" + order.Number, TestContext.Current.CancellationToken));

        ex.ErrorNumber.ShouldBe(ErrorNumbers.IdempotencyKeyConflict);
    }

    [Fact]
    public async Task PostingAnOrderWithoutLinesIsInvalid()
    {
        await using var db = sql.CreateDbContext();
        var supplier = await TestData.AddSupplierAsync(db);
        var order = await TestData.AddDraftOrderAsync(db, supplier, JobSite.WarehouseId);

        var ex = await Should.ThrowAsync<DomainException>(
            () => new StockyardProcedures(db).PostPurchaseOrderAsync(order.Id, "k-" + order.Number, TestContext.Current.CancellationToken));

        ex.ErrorNumber.ShouldBe(ErrorNumbers.Validation);
    }

    [Fact]
    public async Task PostingAnUnknownOrderIsNotFound()
    {
        await using var db = sql.CreateDbContext();

        var ex = await Should.ThrowAsync<DomainException>(
            () => new StockyardProcedures(db).PostPurchaseOrderAsync(Guid.NewGuid(), "k-unknown", TestContext.Current.CancellationToken));

        ex.ErrorNumber.ShouldBe(ErrorNumbers.NotFound);
    }

    [Fact]
    public async Task PostingWritesTheContractPayload()
    {
        await using var db = sql.CreateDbContext();
        var (order, item) = await DraftOrderAsync(db);

        await new StockyardProcedures(db).PostPurchaseOrderAsync(order.Id, "payload-" + order.Number, TestContext.Current.CancellationToken);

        var row = (await OutboxRowsForAsync(db, order.Id)).Single();
        row.Type.ShouldBe(EventTypes.PurchaseOrderPosted);
        var payload = JsonSerializer.Deserialize<PurchaseOrderPosted>(row.Payload, Json)!;
        payload.OrderId.ShouldBe(order.Id);
        payload.Number.ShouldBe(order.Number);
        payload.Lines.Count.ShouldBe(1);
        payload.Lines[0].ItemSku.ShouldBe(item.Sku);
        payload.Lines[0].QuantityOrdered.ShouldBe(40m);
        payload.Lines[0].UnitCost.ShouldBe(item.UnitCost);
    }

    [Fact]
    public async Task SupplierWithoutWebhookGetsNoDelivery()
    {
        await using var db = sql.CreateDbContext();
        var supplier = await TestData.AddSupplierAsync(db, webhookUrl: null);
        var item = await TestData.AddItemAsync(db);
        var order = await TestData.AddDraftOrderAsync(db, supplier, JobSite.WarehouseId, (item, 5));

        await new StockyardProcedures(db).PostPurchaseOrderAsync(order.Id, "nohook-" + order.Number, TestContext.Current.CancellationToken);

        (await db.WebhookDeliveries.CountAsync(w => w.SupplierId == supplier.Id, TestContext.Current.CancellationToken)).ShouldBe(0);
        (await OutboxRowsForAsync(db, order.Id)).Count.ShouldBe(1);
    }

    private static async Task<(PurchaseOrder Order, Item Item)> DraftOrderAsync(Stockyard.Infrastructure.Persistence.StockyardDbContext db)
    {
        var supplier = await TestData.AddSupplierAsync(db);
        var site = await TestData.AddJobSiteAsync(db);
        var item = await TestData.AddItemAsync(db);
        var order = await TestData.AddDraftOrderAsync(db, supplier, site.Id, (item, 40));
        return (order, item);
    }

    private static Task<List<OutboxMessage>> OutboxRowsForAsync(Stockyard.Infrastructure.Persistence.StockyardDbContext db, Guid orderId)
    {
        var id = orderId.ToString().ToUpperInvariant();
        return db.OutboxMessages
            .Where(o => o.Type == EventTypes.PurchaseOrderPosted && o.Payload.Contains(id))
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
