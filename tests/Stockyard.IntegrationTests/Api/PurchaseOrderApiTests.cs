using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stockyard.Domain.Events;

namespace Stockyard.IntegrationTests.Api;

public sealed class PurchaseOrderApiTests(SqlServerFixture sql) : ApiTestBase(sql)
{
    [Fact]
    public async Task ADraftOrderHasItsLinesAndAPurchaseOrderNumber()
    {
        var (supplier, site, itemA, itemB) = await MasterDataAsync();
        var id = await DraftOrderAsync(supplier, site, itemA, itemB);

        var order = await GetJsonAsync($"/api/v1/purchase-orders/{id}");

        order.GetProperty("number").GetString()!.ShouldMatch("^PO-[0-9]{6,}$");
        order.GetProperty("status").GetString().ShouldBe("Draft");
        order.GetProperty("supplierCode").GetString().ShouldBe(supplier);
        order.GetProperty("postedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        var lines = order.GetProperty("lines").EnumerateArray().ToList();
        lines.Select(l => l.GetProperty("lineNumber").GetInt32()).ShouldBe([1, 2]);
        lines[0].GetProperty("unitCost").GetDecimal().ShouldBe(3.45m);
        lines[1].GetProperty("unitCost").GetDecimal().ShouldBe(17.5m);
        lines[1].GetProperty("lineTotal").GetDecimal().ShouldBe(70m);
    }

    [Fact]
    public async Task PostingTwiceWithTheSameKeyReturnsTheSameOrderAndPostsOnce()
    {
        var (supplier, site, itemA, itemB) = await MasterDataAsync("http://fake-supplier.test/webhooks");
        var id = await DraftOrderAsync(supplier, site, itemA, itemB);
        var key = Guid.NewGuid().ToString();

        using var first = await PostOrderAsync(id, key);
        using var second = await PostOrderAsync(id, key);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadAsStringAsync(Ct);
        (await second.Content.ReadAsStringAsync(Ct)).ShouldBe(firstBody);
        JsonDocument.Parse(firstBody).RootElement.GetProperty("status").GetString().ShouldBe("Posted");

        await using var db = Sql.CreateDbContext();
        var orderId = id.ToString().ToUpperInvariant();
        (await db.OutboxMessages.CountAsync(o => o.Type == EventTypes.PurchaseOrderPosted && o.Payload.Contains(orderId), Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task PostingWithoutAnIdempotencyKeyIsABadRequest()
    {
        var (supplier, site, itemA, itemB) = await MasterDataAsync();
        var id = await DraftOrderAsync(supplier, site, itemA, itemB);

        using var missing = await PostOrderAsync(id, null);
        using var tooLong = await PostOrderAsync(id, new string('k', 65));

        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetJsonAsync($"/api/v1/purchase-orders/{id}")).GetProperty("status").GetString().ShouldBe("Draft");
    }

    [Fact]
    public async Task PostingAgainWithADifferentKeyIsAConflict()
    {
        var (supplier, site, itemA, itemB) = await MasterDataAsync();
        var id = await DraftOrderAsync(supplier, site, itemA, itemB);
        using var posted = await PostOrderAsync(id, "first");

        using var again = await PostOrderAsync(id, "second");

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorNumber").GetInt32().ShouldBe(50001);
    }

    [Fact]
    public async Task LinesCannotBeAddedOnceTheOrderIsPosted()
    {
        var (supplier, site, itemA, itemB) = await MasterDataAsync();
        var id = await DraftOrderAsync(supplier, site, itemA, itemB);
        using var posted = await PostOrderAsync(id, Guid.NewGuid().ToString());

        await PostJsonAsync($"/api/v1/purchase-orders/{id}/lines", new { itemSku = itemA, quantityOrdered = 1 }, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ReceivingMovesTheOrderToReceivedAndPutsStockOnHand()
    {
        var (supplier, site, itemA, itemB) = await MasterDataAsync();
        var id = await DraftOrderAsync(supplier, site, itemA, itemB);
        using var posted = await PostOrderAsync(id, Guid.NewGuid().ToString());

        var partial = await PostJsonAsync($"/api/v1/purchase-orders/{id}/receipts", new { lines = new[] { new { lineNumber = 1, quantity = 6 } } }, HttpStatusCode.OK);
        partial.GetProperty("status").GetString().ShouldBe("PartiallyReceived");

        var open = await GetJsonAsync("/api/v1/purchase-orders/open");
        open.EnumerateArray().Where(l => l.GetProperty("purchaseOrderId").GetGuid() == id)
            .Select(l => (l.GetProperty("lineNumber").GetInt32(), l.GetProperty("quantityOutstanding").GetDecimal()))
            .ShouldBe([(1, 4m), (2, 4m)]);

        var received = await PostJsonAsync(
            $"/api/v1/purchase-orders/{id}/receipts",
            new { lines = new[] { new { lineNumber = 1, quantity = 4 }, new { lineNumber = 2, quantity = 4 } } },
            HttpStatusCode.OK);
        received.GetProperty("status").GetString().ShouldBe("Received");

        var onHand = await GetJsonAsync($"/api/v1/stock/on-hand?itemSku={itemA.ToLowerInvariant()}");
        onHand.EnumerateArray().Single().GetProperty("quantity").GetDecimal().ShouldBe(10m);
        onHand.EnumerateArray().Single().GetProperty("jobSiteCode").GetString().ShouldBe(site);
    }

    [Fact]
    public async Task ReceiptErrorsMapToTheirStatusCodes()
    {
        var (supplier, site, itemA, itemB) = await MasterDataAsync();
        var draft = await DraftOrderAsync(supplier, site, itemA, itemB);
        var one = new { lines = new[] { new { lineNumber = 1, quantity = 1 } } };

        await PostJsonAsync($"/api/v1/purchase-orders/{draft}/receipts", one, HttpStatusCode.Conflict);
        await PostJsonAsync($"/api/v1/purchase-orders/{Guid.NewGuid()}/receipts", one, HttpStatusCode.NotFound);

        using var posted = await PostOrderAsync(draft, Guid.NewGuid().ToString());
        await PostJsonAsync($"/api/v1/purchase-orders/{draft}/receipts", new { lines = new[] { new { lineNumber = 1, quantity = 11 } } }, HttpStatusCode.BadRequest);
        await PostJsonAsync($"/api/v1/purchase-orders/{draft}/receipts", new { lines = Array.Empty<object>() }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OrdersCanBeListedByStatus()
    {
        var (supplier, site, itemA, itemB) = await MasterDataAsync();
        var draft = await DraftOrderAsync(supplier, site, itemA, itemB);
        var posted = await DraftOrderAsync(supplier, site, itemA, itemB);
        using var response = await PostOrderAsync(posted, Guid.NewGuid().ToString());

        var postedOrders = await GetJsonAsync("/api/v1/purchase-orders?status=posted");
        var ids = postedOrders.EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).ToList();

        ids.ShouldContain(posted);
        ids.ShouldNotContain(draft);
        await GetJsonAsync("/api/v1/purchase-orders?status=Shipped", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnknownSupplierOrSiteIsNotFound()
    {
        var (supplier, site, _, _) = await MasterDataAsync();

        await PostJsonAsync("/api/v1/purchase-orders", new { supplierCode = "NOPE", jobSiteCode = site }, HttpStatusCode.NotFound);
        await PostJsonAsync("/api/v1/purchase-orders", new { supplierCode = supplier, jobSiteCode = "NOPE" }, HttpStatusCode.NotFound);
    }
}
