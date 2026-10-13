using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Stockyard.Messaging;

/// <summary>
/// A single-process queue for tests and for running the api alone. It has no dead letter queue,
/// so nothing is ever replayed.
/// </summary>
public sealed class InMemoryMessageBus : IMessageBus
{
    private readonly Channel<(MessageEnvelope Envelope, int ReceiveCount)> queue =
        Channel.CreateUnbounded<(MessageEnvelope, int)>();

    private readonly ConcurrentDictionary<string, (MessageEnvelope Envelope, int ReceiveCount)> inFlight = new();

    public Task PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return queue.Writer.WriteAsync((envelope, 0), cancellationToken).AsTask();
    }

    public async Task<IReadOnlyList<ReceivedMessage>> ReceiveAsync(int maxMessages, TimeSpan wait, CancellationToken cancellationToken)
    {
        var received = new List<ReceivedMessage>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(wait);
        try
        {
            if (!await queue.Reader.WaitToReadAsync(timeout.Token))
            {
                return received;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return received;
        }

        while (received.Count < maxMessages && queue.Reader.TryRead(out var item))
        {
            var handle = Guid.NewGuid().ToString("N");
            var receiveCount = item.ReceiveCount + 1;
            inFlight[handle] = (item.Envelope, receiveCount);
            received.Add(new ReceivedMessage(item.Envelope, handle, receiveCount));
        }

        return received;
    }

    public Task AcknowledgeAsync(ReceivedMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        inFlight.TryRemove(message.ReceiptHandle, out _);
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(ReceivedMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return inFlight.TryRemove(message.ReceiptHandle, out var item)
            ? queue.Writer.WriteAsync(item, cancellationToken).AsTask()
            : Task.CompletedTask;
    }

    public Task<long> GetPendingCountAsync(CancellationToken cancellationToken) =>
        Task.FromResult((long)queue.Reader.Count + inFlight.Count);

    public Task<int> ReplayDeadLettersAsync(int maxMessages, CancellationToken cancellationToken) => Task.FromResult(0);
}
