using System.Text;
using Microsoft.Extensions.Options;
using Stockyard.Domain.Webhooks;

namespace Stockyard.Notifier;

public sealed record SendResult(bool Succeeded, int? StatusCode, string? Error);

/// <summary>Signs and posts one delivery. Any 2xx is a success; everything else is retried.</summary>
public sealed class WebhookSender(HttpClient httpClient, IOptions<NotifierOptions> options, TimeProvider timeProvider)
{
    public const string EventHeader = "X-Stockyard-Event";
    public const string DeliveryHeader = "X-Stockyard-Delivery";

    public async Task<SendResult> SendAsync(ClaimedDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        if (string.IsNullOrEmpty(delivery.WebhookUrl))
        {
            return new SendResult(false, null, "The supplier has no webhook URL.");
        }

        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post, delivery.WebhookUrl)
        {
            Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(WebhookSignature.HeaderName, WebhookSignature.CreateHeader(options.Value.SigningSecret, timestamp, delivery.Payload));
        request.Headers.Add(EventHeader, delivery.EventType);
        request.Headers.Add(DeliveryHeader, delivery.Id.ToString());

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var status = (int)response.StatusCode;
            return response.IsSuccessStatusCode
                ? new SendResult(true, status, null)
                : new SendResult(false, status, $"The supplier answered {status} {response.ReasonPhrase}.");
        }
        catch (HttpRequestException ex)
        {
            return new SendResult(false, null, ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SendResult(false, null, $"No answer within {httpClient.Timeout.TotalSeconds:0} seconds.");
        }
    }
}
