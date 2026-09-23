using Microsoft.EntityFrameworkCore;
using Stockyard.Domain.Webhooks;
using Stockyard.Infrastructure.Persistence;

namespace Stockyard.Notifier;

/// <summary>
/// Claims due deliveries with a lease and records outcomes. The lease is the NextAttemptAt
/// column itself: a claim pushes it forward, so another replica skips the row until the lease
/// runs out, and a notifier that dies mid-send gives the row back when it does.
/// </summary>
public sealed class WebhookDeliveryStore(StockyardDbContext db, TimeProvider timeProvider, WebhookRetryPolicy retryPolicy)
{
    public const int LastErrorMaxLength = 1000;

    public async Task<IReadOnlyList<ClaimedDelivery>> ClaimDueAsync(int batchSize, TimeSpan lease, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var leaseUntil = now + lease;

        var claimed = await db.Database
            .SqlQuery<ClaimedRow>(
                $"""
                WITH due AS
                (
                    SELECT TOP ({batchSize}) Id, SupplierId, EventType, Payload, Attempts, NextAttemptAt
                    FROM dbo.WebhookDelivery WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE DeliveredAt IS NULL
                        AND Attempts < {retryPolicy.MaxAttempts}
                        AND (NextAttemptAt IS NULL OR NextAttemptAt <= {now})
                    ORDER BY NextAttemptAt
                )
                UPDATE due
                SET NextAttemptAt = {leaseUntil}
                OUTPUT inserted.Id, inserted.SupplierId, inserted.EventType, inserted.Payload, inserted.Attempts
                """)
            .ToListAsync(cancellationToken);

        if (claimed.Count == 0)
        {
            return [];
        }

        var supplierIds = claimed.Select(c => c.SupplierId).Distinct().ToList();
        var urls = await db.Suppliers
            .Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.WebhookUrl, cancellationToken);

        return [.. claimed.Select(c => new ClaimedDelivery(c.Id, c.SupplierId, c.EventType, c.Payload, c.Attempts, urls.GetValueOrDefault(c.SupplierId)))];
    }

    public Task MarkDeliveredAsync(ClaimedDelivery delivery, int statusCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return db.WebhookDeliveries
            .Where(w => w.Id == delivery.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(w => w.DeliveredAt, now)
                    .SetProperty(w => w.Attempts, delivery.Attempts + 1)
                    .SetProperty(w => w.LastStatusCode, statusCode)
                    .SetProperty(w => w.LastError, (string?)null)
                    .SetProperty(w => w.NextAttemptAt, (DateTime?)null),
                cancellationToken);
    }

    /// <summary>Records a failed attempt and schedules the next one, or none once the attempts run out.</summary>
    public Task MarkFailedAsync(ClaimedDelivery delivery, int? statusCode, string error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(error);

        var attempts = delivery.Attempts + 1;
        DateTime? next = retryPolicy.CanRetry(attempts)
            ? timeProvider.GetUtcNow().UtcDateTime + retryPolicy.DelayAfter(attempts)
            : null;
        var truncated = error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;

        return db.WebhookDeliveries
            .Where(w => w.Id == delivery.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(w => w.Attempts, attempts)
                    .SetProperty(w => w.LastStatusCode, statusCode)
                    .SetProperty(w => w.LastError, truncated)
                    .SetProperty(w => w.NextAttemptAt, next),
                cancellationToken);
    }

    private sealed class ClaimedRow
    {
        public Guid Id { get; init; }

        public Guid SupplierId { get; init; }

        public required string EventType { get; init; }

        public required string Payload { get; init; }

        public int Attempts { get; init; }
    }
}
