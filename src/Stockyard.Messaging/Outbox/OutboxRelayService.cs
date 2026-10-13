using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Stockyard.Messaging.Outbox;

/// <summary>Runs the relay in the api process: full batches back to back, otherwise every poll interval.</summary>
public sealed partial class OutboxRelayService(
    OutboxRelay relay,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxRelayService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await relay.RelayBatchAsync(stoppingToken);
                if (published >= settings.BatchSize)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogRelayFailed(ex);
            }

            try
            {
                await Task.Delay(settings.PollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox relay batch failed")]
    private partial void LogRelayFailed(Exception exception);
}
