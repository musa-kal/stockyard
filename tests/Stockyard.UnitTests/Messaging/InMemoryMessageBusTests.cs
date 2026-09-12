using System.Text.Json;
using Stockyard.Messaging;

namespace Stockyard.UnitTests.Messaging;

public sealed class InMemoryMessageBusTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReceiveReturnsPublishedMessagesInOrder()
    {
        var bus = new InMemoryMessageBus();
        var first = Envelope();
        var second = Envelope();
        await bus.PublishAsync(first, Ct);
        await bus.PublishAsync(second, Ct);

        var received = await bus.ReceiveAsync(10, TimeSpan.FromSeconds(1), Ct);

        received.Select(r => r.Envelope.MessageId).ShouldBe([first.MessageId, second.MessageId]);
        received.ShouldAllBe(r => r.ReceiveCount == 1);
    }

    [Fact]
    public async Task ReceiveReturnsNothingWhenTheWaitExpires()
    {
        var received = await new InMemoryMessageBus().ReceiveAsync(10, TimeSpan.FromMilliseconds(50), Ct);

        received.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReleasedMessagesComeBackWithAHigherReceiveCount()
    {
        var bus = new InMemoryMessageBus();
        await bus.PublishAsync(Envelope(), Ct);
        var first = (await bus.ReceiveAsync(1, TimeSpan.FromSeconds(1), Ct)).Single();

        await bus.ReleaseAsync(first, Ct);
        var second = (await bus.ReceiveAsync(1, TimeSpan.FromSeconds(1), Ct)).Single();

        second.Envelope.MessageId.ShouldBe(first.Envelope.MessageId);
        second.ReceiveCount.ShouldBe(2);
    }

    [Fact]
    public async Task PendingCountsQueuedAndUnacknowledgedMessages()
    {
        var bus = new InMemoryMessageBus();
        await bus.PublishAsync(Envelope(), Ct);
        await bus.PublishAsync(Envelope(), Ct);
        var received = (await bus.ReceiveAsync(1, TimeSpan.FromSeconds(1), Ct)).Single();

        (await bus.GetPendingCountAsync(Ct)).ShouldBe(2);

        await bus.AcknowledgeAsync(received, Ct);
        (await bus.GetPendingCountAsync(Ct)).ShouldBe(1);
    }

    private static MessageEnvelope Envelope()
    {
        using var payload = JsonDocument.Parse("{}");
        return new MessageEnvelope(Guid.NewGuid(), "StockIssued", DateTimeOffset.UtcNow, payload.RootElement.Clone());
    }
}
