using System.Globalization;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;

namespace Stockyard.Messaging;

/// <summary>
/// The events queue on SQS (or LocalStack). The event type travels in the "type" message
/// attribute as well as in the body, so subscriptions and tooling can filter without parsing.
/// </summary>
public sealed class SqsMessageBus(IAmazonSQS sqs, IOptions<MessagingOptions> options) : IMessageBus
{
    public const string TypeAttribute = "type";

    private const int MaxBatch = 10;
    private const int MaxReleaseDelaySeconds = 300;

    private readonly MessagingOptions settings = options.Value;

    public Task PublishAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return SendAsync(settings.QueueUrl, envelope.Serialize(), envelope.Type, cancellationToken);
    }

    public async Task<IReadOnlyList<ReceivedMessage>> ReceiveAsync(int maxMessages, TimeSpan wait, CancellationToken cancellationToken)
    {
        var response = await sqs.ReceiveMessageAsync(
            new ReceiveMessageRequest
            {
                QueueUrl = settings.QueueUrl,
                MaxNumberOfMessages = Math.Clamp(maxMessages, 1, MaxBatch),
                WaitTimeSeconds = (int)Math.Clamp(wait.TotalSeconds, 0, 20),
                MessageSystemAttributeNames = [MessageSystemAttributeName.ApproximateReceiveCount],
            },
            cancellationToken);

        var received = new List<ReceivedMessage>();
        foreach (var message in response.Messages ?? [])
        {
            var receiveCount = message.Attributes is not null
                && message.Attributes.TryGetValue(MessageSystemAttributeName.ApproximateReceiveCount, out var count)
                && int.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : 1;

            MessageEnvelope envelope;
            try
            {
                envelope = MessageEnvelope.Parse(message.Body);
            }
            catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
            {
                // Not ours to handle: leave it invisible until the redrive policy moves it to the DLQ.
                continue;
            }

            received.Add(new ReceivedMessage(envelope, message.ReceiptHandle, receiveCount));
        }

        return received;
    }

    public Task AcknowledgeAsync(ReceivedMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return sqs.DeleteMessageAsync(settings.QueueUrl, message.ReceiptHandle, cancellationToken);
    }

    public Task ReleaseAsync(ReceivedMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Back off a little more on every failed receive instead of waiting out the full timeout.
        var delay = (int)Math.Min(Math.Pow(2, Math.Min(message.ReceiveCount, 9)), MaxReleaseDelaySeconds);
        return sqs.ChangeMessageVisibilityAsync(settings.QueueUrl, message.ReceiptHandle, delay, cancellationToken);
    }

    public async Task<long> GetPendingCountAsync(CancellationToken cancellationToken)
    {
        var response = await sqs.GetQueueAttributesAsync(
            new GetQueueAttributesRequest
            {
                QueueUrl = settings.QueueUrl,
                AttributeNames =
                [
                    QueueAttributeName.ApproximateNumberOfMessages,
                    QueueAttributeName.ApproximateNumberOfMessagesNotVisible,
                ],
            },
            cancellationToken);

        return (long)response.ApproximateNumberOfMessages + response.ApproximateNumberOfMessagesNotVisible;
    }

    public async Task<int> ReplayDeadLettersAsync(int maxMessages, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.DlqUrl))
        {
            throw new InvalidOperationException("Messaging__DlqUrl is not set.");
        }

        var replayed = 0;
        while (replayed < maxMessages)
        {
            var response = await sqs.ReceiveMessageAsync(
                new ReceiveMessageRequest
                {
                    QueueUrl = settings.DlqUrl,
                    MaxNumberOfMessages = Math.Min(MaxBatch, maxMessages - replayed),
                    WaitTimeSeconds = 1,
                    MessageAttributeNames = [TypeAttribute],
                },
                cancellationToken);

            if (response.Messages is not { Count: > 0 } messages)
            {
                break;
            }

            foreach (var message in messages)
            {
                var type = message.MessageAttributes is not null && message.MessageAttributes.TryGetValue(TypeAttribute, out var attribute)
                    ? attribute.StringValue
                    : null;
                await SendAsync(settings.QueueUrl, message.Body, type, cancellationToken);
                await sqs.DeleteMessageAsync(settings.DlqUrl, message.ReceiptHandle, cancellationToken);
                replayed++;
            }
        }

        return replayed;
    }

    private Task<SendMessageResponse> SendAsync(string queueUrl, string body, string? type, CancellationToken cancellationToken)
    {
        var request = new SendMessageRequest { QueueUrl = queueUrl, MessageBody = body };
        if (!string.IsNullOrEmpty(type))
        {
            request.MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                [TypeAttribute] = new() { DataType = "String", StringValue = type },
            };
        }

        return sqs.SendMessageAsync(request, cancellationToken);
    }
}
