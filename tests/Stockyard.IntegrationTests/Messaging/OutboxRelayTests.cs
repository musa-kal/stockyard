using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stockyard.Domain;
using Stockyard.Domain.Events;
using Stockyard.Infrastructure.Outbox;
using Stockyard.Infrastructure.Persistence;
using Stockyard.Messaging;
using Stockyard.Messaging.Outbox;

namespace Stockyard.IntegrationTests.Messaging;

public sealed class OutboxRelayTests(SqlServerFixture sql)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RelayPublishesUnpublishedRowsAndMarksThem()
    {
        var ids = await AddOutboxRowsAsync(3);
        var bus = new InMemoryMessageBus();
        await using var services = Services();

        await DrainAsync(Relay(services, bus));

        var received = await bus.ReceiveAsync(500, TimeSpan.FromSeconds(1), Ct);
        ids.ShouldBeSubsetOf(received.Select(r => r.Envelope.MessageId));
        await using var db = sql.CreateDbContext();
        var rows = await db.OutboxMessages.Where(o => ids.Contains(o.Id)).ToListAsync(Ct);
        rows.ShouldAllBe(o => o.PublishedAt != null && o.Attempts == 1 && o.LastError == null);
    }

    [Fact]
    public async Task AFailedPublishIsRecordedAndRetriedLater()
    {
        var ids = await AddOutboxRowsAsync(1);
        await using var services = Services();

        await DrainAsync(Relay(services, new FailingBus()));

        await using (var db = sql.CreateDbContext())
        {
            var row = await db.OutboxMessages.SingleAsync(o => o.Id == ids[0], Ct);
            row.PublishedAt.ShouldBeNull();
            row.Attempts.ShouldBe(1);
            row.LastError.ShouldBe("queue unavailable");
        }

        var bus = new InMemoryMessageBus();
        await DrainAsync(Relay(services, bus));

        (await bus.ReceiveAsync(500, TimeSpan.FromSeconds(1), Ct)).ShouldContain(r => r.Envelope.MessageId == ids[0]);
    }

    [Fact]
    public async Task ClaimingOutsideATransactionIsRefused()
    {
        await using var db = sql.CreateDbContext();

        await Should.ThrowAsync<InvalidOperationException>(
            () => new OutboxStore(db, TimeProvider.System).LockUnpublishedAsync(10, Ct));
    }

    private static OutboxRelay Relay(IServiceProvider services, IMessageBus bus) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            bus,
            Options.Create(new OutboxOptions { BatchSize = 50 }),
            NullLogger<OutboxRelay>.Instance);

    /// <summary>Other tests leave rows behind, so relay until a batch comes back short.</summary>
    private static async Task DrainAsync(OutboxRelay relay)
    {
        while (await relay.RelayBatchAsync(Ct) == 50)
        {
        }
    }

    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddDbContext<StockyardDbContext>(o => o.UseSqlServer(sql.ConnectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<OutboxStore>();
        return services.BuildServiceProvider();
    }

    private async Task<List<Guid>> AddOutboxRowsAsync(int count)
    {
        await using var db = sql.CreateDbContext();
        var rows = Enumerable.Range(0, count)
            .Select(i => new OutboxMessage
            {
                Type = EventTypes.StockIssued,
                Payload = $$"""{"itemSku":"RELAY-{{i}}","jobSiteCode":"WAREHOUSE","quantity":1,"reference":"relay test"}""",
                OccurredAt = DateTime.UtcNow,
            })
            .ToList();
        db.OutboxMessages.AddRange(rows);
        await db.SaveChangesAsync(Ct);
        return [.. rows.Select(r => r.Id)];
    }

    private sealed class FailingBus : IMessageBus
    {
        public Task PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("queue unavailable");

        public Task<IReadOnlyList<ReceivedMessage>> ReceiveAsync(int maxMessages, TimeSpan wait, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AcknowledgeAsync(ReceivedMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReleaseAsync(ReceivedMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<long> GetPendingCountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> ReplayDeadLettersAsync(int maxMessages, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
