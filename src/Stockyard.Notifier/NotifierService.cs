using Microsoft.Extensions.Options;

namespace Stockyard.Notifier;

/// <summary>Drains due deliveries: full batches back to back, otherwise every poll interval.</summary>
public sealed partial class NotifierService(
    IServiceScopeFactory scopeFactory,
    IOptions<NotifierOptions> options,
    TimeProvider timeProvider,
    ILogger<NotifierService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<WebhookDispatcher>();
                if (await dispatcher.DispatchBatchAsync(stoppingToken) >= settings.BatchSize)
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
                LogBatchFailed(ex);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Webhook batch failed")]
    private partial void LogBatchFailed(Exception exception);
}
