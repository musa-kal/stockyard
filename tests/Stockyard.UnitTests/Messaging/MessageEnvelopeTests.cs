using System.Text.Json;
using Stockyard.Domain;
using Stockyard.Domain.Events;
using Stockyard.Messaging;

namespace Stockyard.UnitTests.Messaging;

public sealed class MessageEnvelopeTests
{
    private static readonly OutboxMessage Row = new()
    {
        Id = Guid.Parse("8f0c2a51-6b7e-4f0e-9a51-2f6d1c0b9e11"),
        Type = EventTypes.StockIssued,
        Payload = """{"itemSku":"VALVE-BV-25","jobSiteCode":"RIVERSIDE","quantity":2.000,"reference":"WO-4471"}""",
        OccurredAt = new DateTime(2026, 9, 1, 14, 30, 0, DateTimeKind.Unspecified),
    };

    [Fact]
    public void SerializesTheDocumentedShape()
    {
        using var json = JsonDocument.Parse(MessageEnvelope.FromOutbox(Row).Serialize());
        var root = json.RootElement;

        root.GetProperty("messageId").GetGuid().ShouldBe(Row.Id);
        root.GetProperty("type").GetString().ShouldBe("StockIssued");
        root.GetProperty("occurredAt").GetDateTimeOffset().ShouldBe(new DateTimeOffset(2026, 9, 1, 14, 30, 0, TimeSpan.Zero));
        root.GetProperty("payload").GetProperty("itemSku").GetString().ShouldBe("VALVE-BV-25");
    }

    [Fact]
    public void RoundTripsThroughParse()
    {
        var envelope = MessageEnvelope.FromOutbox(Row);

        var parsed = MessageEnvelope.Parse(envelope.Serialize());

        parsed.MessageId.ShouldBe(envelope.MessageId);
        parsed.Type.ShouldBe(envelope.Type);
        parsed.PayloadAs<StockIssued>().ShouldBe(new StockIssued("VALVE-BV-25", "RIVERSIDE", 2m, "WO-4471"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"messageId":"00000000-0000-0000-0000-000000000000","type":"StockIssued","occurredAt":"2026-09-01T00:00:00Z","payload":{}}""")]
    [InlineData("""{"messageId":"8f0c2a51-6b7e-4f0e-9a51-2f6d1c0b9e11","occurredAt":"2026-09-01T00:00:00Z","payload":{}}""")]
    public void ParseRejectsBodiesThatAreNotEnvelopes(string body)
    {
        Should.Throw<FormatException>(() => MessageEnvelope.Parse(body));
    }
}
