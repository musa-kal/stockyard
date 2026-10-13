using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Stockyard.Messaging.Consuming;

/// <summary>Long-polls the events queue for as long as the worker runs.</summary>
public sealed partial class MessageConsumerService(
    MessageConsumer consumer,
    TimeProvider timeProvider,
    ILogger<MessageConsumerService> logger) : BackgroundService
{
    private const int BatchSize = 10;
    private static readonly TimeSpan LongPoll = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await consumer.PollOnceAsync(BatchSize, LongPoll, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogPollFailed(ex);
                try
                {
                    await Task.Delay(ErrorBackoff, timeProvider, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Receiving from the events queue failed")]
    private partial void LogPollFailed(Exception exception);
}
