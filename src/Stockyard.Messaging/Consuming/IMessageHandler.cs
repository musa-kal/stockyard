using Stockyard.Infrastructure.Persistence;

namespace Stockyard.Messaging.Consuming;

/// <summary>Applies one message. Runs inside the consumer's transaction, after the guard row is inserted.</summary>
public interface IMessageHandler
{
    /// <summary>The name recorded in ProcessedMessage.ConsumerName.</summary>
    string ConsumerName { get; }

    Task HandleAsync(MessageEnvelope envelope, StockyardDbContext db, CancellationToken cancellationToken);
}
