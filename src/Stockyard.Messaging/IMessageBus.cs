namespace Stockyard.Messaging;

/// <summary>The events queue. Delivery is at least once; consumers deduplicate on the message id.</summary>
public interface IMessageBus
{
    Task PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReceivedMessage>> ReceiveAsync(int maxMessages, TimeSpan wait, CancellationToken cancellationToken);

    /// <summary>Removes a message that was handled, or recognised as a duplicate.</summary>
    Task AcknowledgeAsync(ReceivedMessage message, CancellationToken cancellationToken);

    /// <summary>Hands a message back for another try after a short backoff.</summary>
    Task ReleaseAsync(ReceivedMessage message, CancellationToken cancellationToken);

    /// <summary>Messages waiting or in flight on the events queue.</summary>
    Task<long> GetPendingCountAsync(CancellationToken cancellationToken);

    /// <summary>Moves up to <paramref name="maxMessages"/> dead letters back to the events queue.</summary>
    Task<int> ReplayDeadLettersAsync(int maxMessages, CancellationToken cancellationToken);
}
