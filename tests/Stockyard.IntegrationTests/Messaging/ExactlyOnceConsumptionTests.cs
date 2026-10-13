using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Stockyard.Domain;
using Stockyard.Domain.Events;
using Stockyard.Infrastructure.Persistence;
using Stockyard.Messaging;
using Stockyard.Messaging.Consuming;
using Stockyard.Projections;

namespace Stockyard.IntegrationTests.Messaging;

/// <summary>
/// The projections consumer applies each event once even though SQS delivers at least once.
/// </summary>
public sealed class ExactlyOnceConsumptionTests(SqlServerFixture sql)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheSameMessageDeliveredTwiceIsAppliedOnce()
    {
        var (item, site, envelope) = await ReceivedStockEventAsync(quantity: 9);
        var bus = new InMemoryMessageBus();
        await using var services = Services(bus, sp => new StockProjectionHandler(TimeProvider.System));
        var consumer = services.GetRequiredService<MessageConsumer>();

        await bus.PublishAsync(envelope, Ct);
        await bus.PublishAsync(envelope, Ct);
        var outcomes = new List<ConsumeOutcome>();
        foreach (var message in await bus.ReceiveAsync(10, TimeSpan.FromSeconds(1), Ct))
        {
            outcomes.Add(await consumer.ProcessAsync(message, Ct));
        }

        outcomes.ShouldBe([ConsumeOutcome.Applied, ConsumeOutcome.Duplicate]);
        (await ProjectedAsync(item, site)).ShouldBe(9m);
        (await GuardRowsAsync(envelope.MessageId)).ShouldBe(1);
        (await bus.GetPendingCountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task ConcurrentDeliveriesOfOneMessageApplyItOnce()
    {
        var (item, site, envelope) = await ReceivedStockEventAsync(quantity: 5);
        var bus = new InMemoryMessageBus();
        await using var services = Services(bus, sp => new StockProjectionHandler(TimeProvider.System));
        var consumer = services.GetRequiredService<MessageConsumer>();
        for (var i = 0; i < 4; i++)
        {
            await bus.PublishAsync(envelope, Ct);
        }

        var messages = await bus.ReceiveAsync(10, TimeSpan.FromSeconds(1), Ct);
        var outcomes = await Task.WhenAll(messages.Select(m => Task.Run(() => consumer.ProcessAsync(m, Ct), Ct)));

        outcomes.Count(o => o == ConsumeOutcome.Applied).ShouldBe(1);
        outcomes.Count(o => o == ConsumeOutcome.Duplicate).ShouldBe(3);
        (await ProjectedAsync(item, site)).ShouldBe(5m);
    }

    [Fact]
    public async Task AnInterruptionAfterTheGuardInsertLeavesNothingBehind()
    {
        var (item, site, envelope) = await ReceivedStockEventAsync(quantity: 7);
        var bus = new InMemoryMessageBus();
        await bus.PublishAsync(envelope, Ct);
        var delivery = (await bus.ReceiveAsync(1, TimeSpan.FromSeconds(1), Ct)).Single();

        await using (var crashing = Services(bus, sp => new CrashBeforeCommitHandler(new StockProjectionHandler(TimeProvider.System))))
        {
            await Should.ThrowAsync<SimulatedCrashException>(
                () => crashing.GetRequiredService<MessageConsumer>().ProcessAsync(delivery, Ct));
        }

        // The rolled back transaction took the guard row and the projection change with it.
        (await GuardRowsAsync(envelope.MessageId)).ShouldBe(0);
        (await ProjectionExistsAsync(item, site)).ShouldBeFalse();

        await bus.ReleaseAsync(delivery, Ct);
        var redelivery = (await bus.ReceiveAsync(1, TimeSpan.FromSeconds(1), Ct)).Single();
        redelivery.ReceiveCount.ShouldBe(2);

        await using var healthy = Services(bus, sp => new StockProjectionHandler(TimeProvider.System));
        (await healthy.GetRequiredService<MessageConsumer>().ProcessAsync(redelivery, Ct)).ShouldBe(ConsumeOutcome.Applied);

        (await ProjectedAsync(item, site)).ShouldBe(7m);
        (await GuardRowsAsync(envelope.MessageId)).ShouldBe(1);
    }

    [Fact]
    public async Task IssuesAreSubtractedFromTheProjection()
    {
        var (item, site, received) = await ReceivedStockEventAsync(quantity: 10);
        await using (var db = sql.CreateDbContext())
        {
            await new Stockyard.Infrastructure.Procedures.StockyardProcedures(db).IssueStockAsync(item.Id, site.Id, 3, "WO-88", Ct);
        }

        var issued = await LatestEnvelopeAsync(EventTypes.StockIssued, item.Sku);
        var bus = new InMemoryMessageBus();
        await using var services = Services(bus, sp => new StockProjectionHandler(TimeProvider.System));
        await bus.PublishAsync(received, Ct);
        await bus.PublishAsync(issued, Ct);

        await services.GetRequiredService<MessageConsumer>().PollOnceAsync(10, TimeSpan.FromSeconds(1), Ct);

        (await ProjectedAsync(item, site)).ShouldBe(7m);
    }

    [Fact]
    public async Task OtherEventTypesAreRecordedAndAcknowledged()
    {
        await using var db = sql.CreateDbContext();
        var supplier = await TestData.AddSupplierAsync(db);
        var stock = await TestData.AddItemAsync(db);
        var order = await TestData.AddDraftOrderAsync(db, supplier, JobSite.WarehouseId, (stock, 1));
        await new Stockyard.Infrastructure.Procedures.StockyardProcedures(db).PostPurchaseOrderAsync(order.Id, "other-" + order.Number, Ct);
        var posted = await LatestEnvelopeAsync(EventTypes.PurchaseOrderPosted, order.Number);
        var bus = new InMemoryMessageBus();
        await using var services = Services(bus, sp => new StockProjectionHandler(TimeProvider.System));
        await bus.PublishAsync(posted, Ct);

        await services.GetRequiredService<MessageConsumer>().PollOnceAsync(10, TimeSpan.FromSeconds(1), Ct);

        (await GuardRowsAsync(posted.MessageId)).ShouldBe(1);
        (await bus.GetPendingCountAsync(Ct)).ShouldBe(0);
    }

    private ServiceProvider Services(IMessageBus bus, Func<IServiceProvider, IMessageHandler> handler)
    {
        var services = new ServiceCollection();
        services.AddDbContext<StockyardDbContext>(o => o.UseSqlServer(sql.ConnectionString));
        services.AddSingleton(bus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddScoped(handler);
        services.AddSingleton<MessageConsumer>();
        return services.BuildServiceProvider();
    }

    private async Task<(Item Item, JobSite Site, MessageEnvelope Envelope)> ReceivedStockEventAsync(decimal quantity)
    {
        await using var db = sql.CreateDbContext();
        var item = await TestData.AddItemAsync(db);
        var site = await TestData.AddJobSiteAsync(db);
        var order = await TestData.ReceiveStockAsync(db, item, site.Id, quantity);
        return (item, site, await LatestEnvelopeAsync(EventTypes.DeliveryReceived, order.Id.ToString().ToUpperInvariant()));
    }

    private async Task<MessageEnvelope> LatestEnvelopeAsync(string type, string payloadContains)
    {
        await using var db = sql.CreateDbContext();
        var row = await db.OutboxMessages
            .Where(o => o.Type == type && o.Payload.Contains(payloadContains))
            .OrderByDescending(o => o.OccurredAt)
            .FirstAsync(Ct);
        return MessageEnvelope.FromOutbox(row);
    }

    private async Task<int> GuardRowsAsync(Guid messageId)
    {
        await using var db = sql.CreateDbContext();
        return await db.ProcessedMessages.CountAsync(
            p => p.ConsumerName == StockProjectionHandler.Name && p.MessageId == messageId, Ct);
    }

    private async Task<decimal> ProjectedAsync(Item item, JobSite site)
    {
        await using var db = sql.CreateDbContext();
        return await db.StockOnHand
            .Where(s => s.ItemId == item.Id && s.JobSiteId == site.Id)
            .Select(s => s.Quantity)
            .SingleAsync(Ct);
    }

    private async Task<bool> ProjectionExistsAsync(Item item, JobSite site)
    {
        await using var db = sql.CreateDbContext();
        return await db.StockOnHand.AnyAsync(s => s.ItemId == item.Id && s.JobSiteId == site.Id, Ct);
    }

    /// <summary>
    /// Applies the real change, checks the guard row is there inside the transaction, then fails
    /// the way a killed process would: before the commit.
    /// </summary>
    private sealed class CrashBeforeCommitHandler(IMessageHandler inner) : IMessageHandler
    {
        public string ConsumerName => inner.ConsumerName;

        public async Task HandleAsync(MessageEnvelope envelope, StockyardDbContext db, CancellationToken cancellationToken)
        {
            await inner.HandleAsync(envelope, db, cancellationToken);
            var guarded = await db.ProcessedMessages.AnyAsync(
                p => p.ConsumerName == ConsumerName && p.MessageId == envelope.MessageId, cancellationToken);
            guarded.ShouldBeTrue();
            throw new SimulatedCrashException();
        }
    }

    private sealed class SimulatedCrashException : Exception
    {
        public SimulatedCrashException()
            : base("Simulated crash between the guard insert and the commit.")
        {
        }
    }
}
