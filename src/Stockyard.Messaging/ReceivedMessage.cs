namespace Stockyard.Messaging;

/// <summary>A message taken from the queue, with what is needed to acknowledge or release it.</summary>
public sealed record ReceivedMessage(MessageEnvelope Envelope, string ReceiptHandle, int ReceiveCount);
