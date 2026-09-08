using Microsoft.EntityFrameworkCore;
using Stockyard.Domain;
using Stockyard.Infrastructure.Persistence;

namespace Stockyard.Infrastructure.Outbox;

/// <summary>
/// Reads and settles outbox rows for the relay. Rows are claimed with UPDLOCK and READPAST inside
/// the caller's transaction, so several api replicas can relay at once without sending a row twice
/// in the normal case. A crash between publish and commit can still send a row again; consumers
/// deduplicate on the message id.
/// </summary>
public sealed class OutboxStore(StockyardDbContext db, TimeProvider timeProvider)
{
    public const int LastErrorMaxLength = 1000;

    /// <summary>Locks up to <paramref name="batchSize"/> unpublished rows, oldest first. Requires an open transaction.</summary>
    public async Task<IReadOnlyList<OutboxMessage>> LockUnpublishedAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Outbox rows must be claimed inside a transaction.");
        }

        return await db.OutboxMessages
            .FromSql(
                $"""
                SELECT TOP ({batchSize}) Id, Type, Payload, OccurredAt, PublishedAt, Attempts, LastError
                FROM dbo.OutboxMessage WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE PublishedAt IS NULL
                ORDER BY OccurredAt, Id
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public Task MarkPublishedAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return db.OutboxMessages
            .Where(o => o.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(o => o.PublishedAt, now)
                    .SetProperty(o => o.Attempts, o => o.Attempts + 1)
                    .SetProperty(o => o.LastError, (string?)null),
                cancellationToken);
    }

    public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(error);
        var truncated = error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;
        return db.OutboxMessages
            .Where(o => o.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(o => o.Attempts, o => o.Attempts + 1)
                    .SetProperty(o => o.LastError, truncated),
                cancellationToken);
    }

    public Task<int> CountUnpublishedAsync(CancellationToken cancellationToken) =>
        db.OutboxMessages.CountAsync(o => o.PublishedAt == null, cancellationToken);
}
