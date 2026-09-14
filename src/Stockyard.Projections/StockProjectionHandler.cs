using Microsoft.EntityFrameworkCore;
using Stockyard.Domain.Events;
using Stockyard.Infrastructure.Persistence;
using Stockyard.Messaging;
using Stockyard.Messaging.Consuming;

namespace Stockyard.Projections;

/// <summary>
/// Maintains StockOnHand from DeliveryReceived and StockIssued. Other event types only get the
/// guard row and are acknowledged. Runs inside the consumer's transaction, after the guard insert.
/// </summary>
public sealed class StockProjectionHandler(TimeProvider timeProvider) : IMessageHandler
{
    /// <summary>Also referenced by dbo.usp_ReconcileStock to find events not applied yet.</summary>
    public const string Name = "projections";

    public string ConsumerName => Name;

    public async Task HandleAsync(MessageEnvelope envelope, StockyardDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(db);

        switch (envelope.Type)
        {
            case EventTypes.DeliveryReceived:
                var delivery = envelope.PayloadAs<DeliveryReceived>();
                foreach (var line in delivery.Lines)
                {
                    await ApplyAsync(db, line.ItemSku, delivery.JobSiteCode, line.Quantity, cancellationToken);
                }

                break;

            case EventTypes.StockIssued:
                var issue = envelope.PayloadAs<StockIssued>();
                await ApplyAsync(db, issue.ItemSku, issue.JobSiteCode, -issue.Quantity, cancellationToken);
                break;
        }
    }

    private Task<int> ApplyAsync(StockyardDbContext db, string itemSku, string jobSiteCode, decimal delta, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // UPDLOCK and SERIALIZABLE on the update make the "update, else insert" safe when two
        // replicas touch a new item and site pair at the same time.
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DECLARE @ItemId uniqueidentifier = (SELECT Id FROM dbo.Item WHERE Sku = {itemSku});
            DECLARE @JobSiteId uniqueidentifier = (SELECT Id FROM dbo.JobSite WHERE Code = {jobSiteCode});
            IF @ItemId IS NULL OR @JobSiteId IS NULL
                THROW 50004, N'The event names an item or job site that does not exist.', 1;

            UPDATE dbo.StockOnHand WITH (UPDLOCK, SERIALIZABLE)
            SET Quantity = Quantity + {delta}, UpdatedAt = {now}
            WHERE ItemId = @ItemId AND JobSiteId = @JobSiteId;

            IF @@ROWCOUNT = 0
                INSERT dbo.StockOnHand (ItemId, JobSiteId, Quantity, UpdatedAt)
                VALUES (@ItemId, @JobSiteId, {delta}, {now});
            """,
            cancellationToken);
    }
}
