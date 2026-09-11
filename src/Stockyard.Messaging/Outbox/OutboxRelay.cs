using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stockyard.Infrastructure.Outbox;
using Stockyard.Infrastructure.Persistence;

namespace Stockyard.Messaging.Outbox;

/// <summary>
/// Moves committed outbox rows to the message bus. One batch runs in one transaction: rows are
/// claimed, published one by one and marked, then the transaction commits. A failed publish is
/// recorded on the row and ends the batch, so a queue outage costs one call per poll, not fifty.
/// </summary>
public sealed partial class OutboxRelay(
    IServiceScopeFactory scopeFactory,
    IMessageBus bus,
    IOptions<OutboxOptions> options,
    ILogger<OutboxRelay> logger)
{
    /// <summary>Relays one batch and returns how many rows were published.</summary>
    public async Task<int> RelayBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StockyardDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<OutboxStore>();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var batch = await store.LockUnpublishedAsync(options.Value.BatchSize, cancellationToken);

        var published = 0;
        foreach (var message in batch)
        {
            try
            {
                await bus.PublishAsync(MessageEnvelope.FromOutbox(message), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPublishFailed(message.Id, message.Type, ex);
                await store.MarkFailedAsync(message.Id, ex.Message, cancellationToken);
                break;
            }

            await store.MarkPublishedAsync(message.Id, cancellationToken);
            published++;
        }

        await transaction.CommitAsync(cancellationToken);
        if (published > 0)
        {
            LogPublished(published);
        }

        return published;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing outbox message {MessageId} ({Type}) failed; it stays unpublished")]
    private partial void LogPublishFailed(Guid messageId, string type, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Published {Count} outbox messages")]
    private partial void LogPublished(int count);
}
