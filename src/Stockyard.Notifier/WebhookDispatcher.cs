using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Stockyard.Notifier;

/// <summary>Claims a batch of due deliveries, sends each one and records what happened.</summary>
public sealed partial class WebhookDispatcher(
    WebhookDeliveryStore store,
    WebhookSender sender,
    IOptions<NotifierOptions> options,
    ILogger<WebhookDispatcher> logger)
{
    /// <summary>Returns how many deliveries were attempted.</summary>
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var deliveries = await store.ClaimDueAsync(settings.BatchSize, settings.Lease, cancellationToken);

        foreach (var delivery in deliveries)
        {
            var result = await sender.SendAsync(delivery, cancellationToken);
            if (result.Succeeded)
            {
                await store.MarkDeliveredAsync(delivery, result.StatusCode ?? 200, cancellationToken);
                LogDelivered(delivery.Id, delivery.EventType, delivery.Attempts + 1);
            }
            else
            {
                await store.MarkFailedAsync(delivery, result.StatusCode, result.Error ?? "Unknown failure.", cancellationToken);
                LogFailed(delivery.Id, delivery.EventType, delivery.Attempts + 1, result.Error);
            }
        }

        return deliveries.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Delivered webhook {DeliveryId} ({EventType}) on attempt {Attempt}")]
    private partial void LogDelivered(Guid deliveryId, string eventType, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook {DeliveryId} ({EventType}) failed on attempt {Attempt}: {Error}")]
    private partial void LogFailed(Guid deliveryId, string eventType, int attempt, string? error);
}
