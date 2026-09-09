using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Stockyard.Domain;
using Stockyard.Domain.Events;
using Stockyard.Infrastructure.Procedures;

namespace Stockyard.IntegrationTests.Infrastructure;

public sealed class IssueStockTests(SqlServerFixture sql)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task IssueWritesANegativeMovementAndAPositiveEvent()
    {
        await using var db = sql.CreateDbContext();
        var item = await TestData.AddItemAsync(db);
        var site = await TestData.AddJobSiteAsync(db);
        await TestData.ReceiveStockAsync(db, item, site.Id, 12);

        await new StockyardProcedures(db).IssueStockAsync(item.Id, site.Id, 5, "WO-4471", Ct);

        var issue = await db.StockMovements.SingleAsync(m => m.ItemId == item.Id && m.Kind == MovementKind.Issue, Ct);
        issue.Quantity.ShouldBe(-5m);
        issue.JobSiteId.ShouldBe(site.Id);
        issue.Reference.ShouldBe("WO-4471");

        var row = await db.OutboxMessages.SingleAsync(o => o.Type == EventTypes.StockIssued && o.Payload.Contains(item.Sku), Ct);
        var payload = JsonSerializer.Deserialize<StockIssued>(row.Payload, JsonSerializerOptions.Web);
        payload.ShouldBe(new StockIssued(item.Sku, site.Code, 5m, "WO-4471"));
    }

    [Fact]
    public async Task IssuingMoreThanIsOnHandIsRejected()
    {
        await using var db = sql.CreateDbContext();
        var item = await TestData.AddItemAsync(db);
        await TestData.ReceiveStockAsync(db, item, JobSite.WarehouseId, 3);

        var ex = await Should.ThrowAsync<DomainException>(
            () => new StockyardProcedures(db).IssueStockAsync(item.Id, JobSite.WarehouseId, 3.001m, "WO-1", Ct));

        ex.ErrorNumber.ShouldBe(ErrorNumbers.InsufficientStock);
        (await db.StockMovements.CountAsync(m => m.ItemId == item.Id && m.Kind == MovementKind.Issue, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task StockAtAnotherSiteDoesNotCount()
    {
        await using var db = sql.CreateDbContext();
        var item = await TestData.AddItemAsync(db);
        var site = await TestData.AddJobSiteAsync(db);
        await TestData.ReceiveStockAsync(db, item, JobSite.WarehouseId, 50);

        var ex = await Should.ThrowAsync<DomainException>(
            () => new StockyardProcedures(db).IssueStockAsync(item.Id, site.Id, 1, "WO-2", Ct));

        ex.ErrorNumber.ShouldBe(ErrorNumbers.InsufficientStock);
    }

    [Fact]
    public async Task UnknownItemOrSiteIsNotFound()
    {
        await using var db = sql.CreateDbContext();
        var item = await TestData.AddItemAsync(db);
        var procedures = new StockyardProcedures(db);

        (await Should.ThrowAsync<DomainException>(() => procedures.IssueStockAsync(Guid.NewGuid(), JobSite.WarehouseId, 1, "WO-3", Ct)))
            .ErrorNumber.ShouldBe(ErrorNumbers.NotFound);
        (await Should.ThrowAsync<DomainException>(() => procedures.IssueStockAsync(item.Id, Guid.NewGuid(), 1, "WO-3", Ct)))
            .ErrorNumber.ShouldBe(ErrorNumbers.NotFound);
    }

    [Theory]
    [InlineData(0, "WO-5")]
    [InlineData(-1, "WO-5")]
    [InlineData(1, "")]
    public async Task NonPositiveQuantityOrMissingReferenceIsInvalid(int quantity, string reference)
    {
        await using var db = sql.CreateDbContext();
        var item = await TestData.AddItemAsync(db);

        var ex = await Should.ThrowAsync<DomainException>(
            () => new StockyardProcedures(db).IssueStockAsync(item.Id, JobSite.WarehouseId, quantity, reference, Ct));

        ex.ErrorNumber.ShouldBe(ErrorNumbers.Validation);
    }

    [Fact]
    public async Task ConcurrentIssuesNeverOverdraw()
    {
        const int Tasks = 12;
        const decimal Received = 10m;
        const decimal EachIssue = 2m;

        Guid itemId;
        Guid siteId;
        await using (var setup = sql.CreateDbContext())
        {
            var item = await TestData.AddItemAsync(setup);
            var site = await TestData.AddJobSiteAsync(setup);
            await TestData.ReceiveStockAsync(setup, item, site.Id, Received);
            (itemId, siteId) = (item.Id, site.Id);
        }

        using var start = new ManualResetEventSlim();
        var attempts = Enumerable.Range(0, Tasks).Select(i => Task.Run(
            async () =>
            {
                await using var db = sql.CreateDbContext();
                start.Wait(Ct);
                try
                {
                    await new StockyardProcedures(db).IssueStockAsync(itemId, siteId, EachIssue, $"CREW-{i}", Ct);
                    return true;
                }
                catch (DomainException ex) when (ex.ErrorNumber == ErrorNumbers.InsufficientStock)
                {
                    return false;
                }
            },
            Ct)).ToList();
        start.Set();
        var results = await Task.WhenAll(attempts);

        results.Count(succeeded => succeeded).ShouldBe((int)(Received / EachIssue));
        await using var check = sql.CreateDbContext();
        var balance = await check.StockMovements
            .Where(m => m.ItemId == itemId && m.JobSiteId == siteId)
            .SumAsync(m => m.Quantity, Ct);
        balance.ShouldBe(0m);
    }
}
