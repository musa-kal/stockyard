using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stockyard.Domain;
using Stockyard.Infrastructure.Persistence;

namespace Stockyard.Messaging.Consuming;

public enum ConsumeOutcome
{
    Applied,
    Duplicate,
}

/// <summary>
/// Exactly-once effects on top of at-least-once delivery. The ProcessedMessage guard row and the
/// handler's changes commit in one transaction: a second delivery of the same message fails on
/// the guard's primary key, rolls back and is acknowledged; a crash before the commit leaves
/// neither the guard nor the change behind, so the redelivery applies it once.
/// </summary>
public sealed partial class MessageConsumer(
    IServiceScopeFactory scopeFactory,
    IMessageBus bus,
    TimeProvider timeProvider,
    ILogger<MessageConsumer> logger)
{
    private const int PrimaryKeyViolation = 2627;
    private const int UniqueIndexViolation = 2601;

    public async Task<ConsumeOutcome> ProcessAsync(ReceivedMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StockyardDbContext>();
        var handler = scope.ServiceProvider.GetRequiredService<IMessageHandler>();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.ProcessedMessages.Add(new ProcessedMessage
        {
            ConsumerName = handler.ConsumerName,
            MessageId = message.Envelope.MessageId,
            ProcessedAt = timeProvider.GetUtcNow().UtcDateTime,
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            await bus.AcknowledgeAsync(message, cancellationToken);
            LogDuplicate(message.Envelope.MessageId, message.Envelope.Type);
            return ConsumeOutcome.Duplicate;
        }

        await handler.HandleAsync(message.Envelope, db, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // A crash here redelivers the message, which then takes the duplicate path above.
        await bus.AcknowledgeAsync(message, cancellationToken);
        return ConsumeOutcome.Applied;
    }

    /// <summary>Receives one batch and processes it; failed messages are released for a retry.</summary>
    public async Task<int> PollOnceAsync(int maxMessages, TimeSpan wait, CancellationToken cancellationToken)
    {
        var messages = await bus.ReceiveAsync(maxMessages, wait, cancellationToken);
        foreach (var message in messages)
        {
            try
            {
                await ProcessAsync(message, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(message.Envelope.MessageId, message.Envelope.Type, message.ReceiveCount, ex);
                await bus.ReleaseAsync(message, cancellationToken);
            }
        }

        return messages.Count;
    }

    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: PrimaryKeyViolation or UniqueIndexViolation };

    [LoggerMessage(Level = LogLevel.Information, Message = "Message {MessageId} ({Type}) was already processed; acknowledged")]
    private partial void LogDuplicate(Guid messageId, string type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message {MessageId} ({Type}) failed on receive {ReceiveCount}; released for retry")]
    private partial void LogFailed(Guid messageId, string type, int receiveCount, Exception exception);
}
