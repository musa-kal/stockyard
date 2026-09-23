using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Stockyard.IntegrationTests.Api;

public sealed class StockApiTests(SqlServerFixture sql) : ApiTestBase(sql)
{
    [Fact]
    public async Task IssuingStockReturnsTheMovementAndLowersStockOnHand()
    {
        var (site, item) = await StockAtSiteAsync(10);

        using var response = await Client.PostAsync(
            "/api/v1/stock/issues",
            System.Net.Http.Json.JsonContent.Create(new { itemSku = item.ToLowerInvariant(), jobSiteCode = site, quantity = 3.5m, reference = "WO-1182" }),
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var issue = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        issue.GetProperty("id").GetInt64().ShouldBeGreaterThan(0);
        issue.GetProperty("itemSku").GetString().ShouldBe(item);
        issue.GetProperty("quantity").GetDecimal().ShouldBe(3.5m);

        var onHand = await GetJsonAsync(response.Headers.Location!.OriginalString);
        onHand.EnumerateArray().Single().GetProperty("quantity").GetDecimal().ShouldBe(6.5m);
    }

    [Fact]
    public async Task IssuingMoreThanIsOnHandIsAConflict()
    {
        var (site, item) = await StockAtSiteAsync(2);

        var problem = await PostJsonAsync(
            "/api/v1/stock/issues",
            new { itemSku = item, jobSiteCode = site, quantity = 3, reference = "WO-1183" },
            HttpStatusCode.Conflict);

        problem.GetProperty("errorNumber").GetInt32().ShouldBe(50003);
    }

    [Theory]
    [InlineData(0, "WO-1")]
    [InlineData(1, "")]
    [InlineData(1, "a reference that is far longer than forty characters")]
    public async Task InvalidIssuesAreBadRequests(decimal quantity, string reference)
    {
        var (site, item) = await StockAtSiteAsync(5);

        await PostJsonAsync("/api/v1/stock/issues", new { itemSku = item, jobSiteCode = site, quantity, reference }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IssuingAnUnknownItemIsNotFound()
    {
        await PostJsonAsync(
            "/api/v1/stock/issues",
            new { itemSku = "NO-SUCH-ITEM", jobSiteCode = "WAREHOUSE", quantity = 1, reference = "WO-1" },
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OnHandCanBeFilteredBySite()
    {
        var (site, item) = await StockAtSiteAsync(4);

        var atSite = await GetJsonAsync($"/api/v1/stock/on-hand?itemSku={item}&jobSiteCode={site.ToLowerInvariant()}");
        var atWarehouse = await GetJsonAsync($"/api/v1/stock/on-hand?itemSku={item}&jobSiteCode=WAREHOUSE");

        atSite.GetArrayLength().ShouldBe(1);
        atWarehouse.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task ReconcileIsReadOnlyAndReportsPendingWork()
    {
        var (site, item) = await StockAtSiteAsync(4);

        var report = await GetJsonAsync("/api/v1/stock/reconcile");

        report.GetProperty("differences").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Array);
        report.GetProperty("pendingMessages").GetInt64().ShouldBeGreaterThanOrEqualTo(0);
        report.GetProperty("unpublishedOutboxMessages").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        report.GetProperty("differences").EnumerateArray()
            .ShouldNotContain(d => d.GetProperty("itemSku").GetString() == item && d.GetProperty("jobSiteCode").GetString() == site);
    }

    [Fact]
    public async Task AWebhookDeliveryCanBeReplayed()
    {
        var (supplier, site, itemA, itemB) = await MasterDataAsync("http://fake-supplier.test/webhooks");
        var order = await DraftOrderAsync(supplier, site, itemA, itemB);
        using var posted = await PostOrderAsync(order, Guid.NewGuid().ToString());
        Guid deliveryId;
        await using (var db = Sql.CreateDbContext())
        {
            var supplierId = await db.Suppliers.Where(s => s.Code == supplier).Select(s => s.Id).SingleAsync(Ct);
            deliveryId = await db.WebhookDeliveries.Where(w => w.SupplierId == supplierId).Select(w => w.Id).SingleAsync(Ct);
            await db.WebhookDeliveries.Where(w => w.Id == deliveryId)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.Attempts, 8).SetProperty(w => w.LastError, "gave up"), Ct);
        }

        var replay = await PostJsonAsync($"/api/v1/webhooks/{deliveryId}/replay", new { }, HttpStatusCode.Accepted);

        replay.GetProperty("id").GetGuid().ShouldBe(deliveryId);
        await using var check = Sql.CreateDbContext();
        var delivery = await check.WebhookDeliveries.SingleAsync(w => w.Id == deliveryId, Ct);
        delivery.Attempts.ShouldBe(0);
        delivery.LastError.ShouldBeNull();
        delivery.NextAttemptAt.ShouldNotBeNull();
        await PostJsonAsync($"/api/v1/webhooks/{Guid.NewGuid()}/replay", new { }, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeadLetterReplayWithTheInMemoryBusReplaysNothing()
    {
        var result = await PostJsonAsync("/api/v1/outbox/dlq/replay", new { }, HttpStatusCode.OK);

        result.GetProperty("replayed").GetInt32().ShouldBe(0);
        await PostJsonAsync("/api/v1/outbox/dlq/replay?max=0", new { }, HttpStatusCode.BadRequest);
    }

    private async Task<(string Site, string Item)> StockAtSiteAsync(decimal quantity)
    {
        var supplier = TestData.UniqueCode("S");
        var site = TestData.UniqueCode("J");
        var item = TestData.UniqueCode("PIPE-");
        await PostJsonAsync("/api/v1/suppliers", new { code = supplier, name = "Northgate Pipe Supply" });
        await PostJsonAsync("/api/v1/job-sites", new { code = site, name = "Lakeview School" });
        await PostJsonAsync("/api/v1/items", new { sku = item, description = "Copper pipe 15 mm", unitOfMeasure = "M", unitCost = 4.1m, reorderPoint = 20 });
        var order = await PostJsonAsync("/api/v1/purchase-orders", new { supplierCode = supplier, jobSiteCode = site });
        var id = order.GetProperty("id").GetGuid();
        await PostJsonAsync($"/api/v1/purchase-orders/{id}/lines", new { itemSku = item, quantityOrdered = quantity });
        using var posted = await PostOrderAsync(id, Guid.NewGuid().ToString());
        await PostJsonAsync($"/api/v1/purchase-orders/{id}/receipts", new { lines = new[] { new { lineNumber = 1, quantity } } }, HttpStatusCode.OK);
        return (site, item);
    }
}
