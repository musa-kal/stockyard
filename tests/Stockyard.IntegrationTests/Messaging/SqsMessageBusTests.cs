using System.Text.Json;
using Amazon.SQS.Model;
using Microsoft.Extensions.Options;
using Stockyard.Domain.Events;
using Stockyard.Messaging;

namespace Stockyard.IntegrationTests.Messaging;

public sealed class SqsMessageBusTests(LocalStackFixture localStack) : IClassFixture<LocalStackFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PublishedMessagesCarryTheTypeAttribute()
    {
        var (queueUrl, _) = await localStack.CreateQueuesAsync("events-type");
        using var sqs = localStack.CreateSqsClient();
        var bus = Bus(sqs, queueUrl);
        var envelope = Envelope(EventTypes.DeliveryReceived);

        await bus.PublishAsync(envelope, Ct);

        var raw = await sqs.ReceiveMessageAsync(
            new ReceiveMessageRequest { QueueUrl = queueUrl, MessageAttributeNames = ["All"], WaitTimeSeconds = 2 },
            Ct);
        var message = raw.Messages.ShouldHaveSingleItem();
        message.MessageAttributes[SqsMessageBus.TypeAttribute].StringValue.ShouldBe(EventTypes.DeliveryReceived);
        MessageEnvelope.Parse(message.Body).MessageId.ShouldBe(envelope.MessageId);
    }

    [Fact]
    public async Task AcknowledgedMessagesLeaveTheQueue()
    {
        var (queueUrl, _) = await localStack.CreateQueuesAsync("events-ack");
        using var sqs = localStack.CreateSqsClient();
        var bus = Bus(sqs, queueUrl);
        await bus.PublishAsync(Envelope(EventTypes.StockIssued), Ct);

        var received = (await bus.ReceiveAsync(10, TimeSpan.FromSeconds(2), Ct)).ShouldHaveSingleItem();
        received.ReceiveCount.ShouldBe(1);
        (await bus.GetPendingCountAsync(Ct)).ShouldBe(1);

        await bus.AcknowledgeAsync(received, Ct);

        (await bus.GetPendingCountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task ReleasedMessagesAreDeliveredAgain()
    {
        var (queueUrl, _) = await localStack.CreateQueuesAsync("events-release");
        using var sqs = localStack.CreateSqsClient();
        var bus = Bus(sqs, queueUrl);
        var envelope = Envelope(EventTypes.StockIssued);
        await bus.PublishAsync(envelope, Ct);

        var first = (await bus.ReceiveAsync(1, TimeSpan.FromSeconds(2), Ct)).ShouldHaveSingleItem();
        await bus.ReleaseAsync(first, Ct);
        var second = (await bus.ReceiveAsync(1, TimeSpan.FromSeconds(5), Ct)).ShouldHaveSingleItem();

        second.Envelope.MessageId.ShouldBe(envelope.MessageId);
        second.ReceiveCount.ShouldBe(2);
    }

    [Fact]
    public async Task DeadLettersAreReplayedWithTheirType()
    {
        var (queueUrl, dlqUrl) = await localStack.CreateQueuesAsync("events-replay");
        using var sqs = localStack.CreateSqsClient();
        var envelope = Envelope(EventTypes.StockIssued);
        await sqs.SendMessageAsync(
            new SendMessageRequest
            {
                QueueUrl = dlqUrl,
                MessageBody = envelope.Serialize(),
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    [SqsMessageBus.TypeAttribute] = new() { DataType = "String", StringValue = envelope.Type },
                },
            },
            Ct);
        var bus = Bus(sqs, queueUrl, dlqUrl);

        (await bus.ReplayDeadLettersAsync(100, Ct)).ShouldBe(1);

        var replayed = (await bus.ReceiveAsync(10, TimeSpan.FromSeconds(2), Ct)).ShouldHaveSingleItem();
        replayed.Envelope.MessageId.ShouldBe(envelope.MessageId);
        (await sqs.GetQueueAttributesAsync(dlqUrl, ["ApproximateNumberOfMessages"], Ct)).ApproximateNumberOfMessages.ShouldBe(0);
    }

    [Fact]
    public async Task BodiesThatAreNotEnvelopesAreLeftForTheRedrivePolicy()
    {
        var (queueUrl, _) = await localStack.CreateQueuesAsync("events-poison");
        using var sqs = localStack.CreateSqsClient();
        await sqs.SendMessageAsync(queueUrl, "not json", Ct);

        var received = await Bus(sqs, queueUrl).ReceiveAsync(10, TimeSpan.FromSeconds(2), Ct);

        received.ShouldBeEmpty();
    }

    private static SqsMessageBus Bus(Amazon.SQS.IAmazonSQS sqs, string queueUrl, string dlqUrl = "") =>
        new(sqs, Options.Create(new MessagingOptions { QueueUrl = queueUrl, DlqUrl = dlqUrl }));

    private static MessageEnvelope Envelope(string type)
    {
        using var payload = JsonDocument.Parse("""{"itemSku":"PIPE-CU-15","jobSiteCode":"WAREHOUSE","quantity":1,"reference":"sqs test"}""");
        return new MessageEnvelope(Guid.NewGuid(), type, DateTimeOffset.UtcNow, payload.RootElement.Clone());
    }
}
