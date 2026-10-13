using System.Text.Json;
using Stockyard.Domain;

namespace Stockyard.Messaging;

/// <summary>
/// What travels on the queue: <c>{ messageId, type, occurredAt, payload }</c>. The message id is
/// the outbox row id, so every delivery of the same event carries the same id.
/// </summary>
public sealed record MessageEnvelope(Guid MessageId, string Type, DateTimeOffset OccurredAt, JsonElement Payload)
{
    public static MessageEnvelope FromOutbox(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var payload = JsonDocument.Parse(message.Payload);
        var occurredAt = new DateTimeOffset(DateTime.SpecifyKind(message.OccurredAt, DateTimeKind.Utc));
        return new MessageEnvelope(message.Id, message.Type, occurredAt, payload.RootElement.Clone());
    }

    public static MessageEnvelope Parse(string body)
    {
        var envelope = JsonSerializer.Deserialize<MessageEnvelope>(body, JsonSerializerOptions.Web);
        if (envelope is null || envelope.MessageId == Guid.Empty || string.IsNullOrEmpty(envelope.Type))
        {
            throw new FormatException("The message body is not a Stockyard envelope.");
        }

        return envelope;
    }

    public string Serialize() => JsonSerializer.Serialize(this, JsonSerializerOptions.Web);

    public T PayloadAs<T>() =>
        Payload.Deserialize<T>(JsonSerializerOptions.Web)
        ?? throw new FormatException($"The {Type} payload is empty.");
}
