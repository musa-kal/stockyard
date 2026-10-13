namespace Stockyard.Domain.Events;

public sealed record DeliveryReceived(Guid OrderId, string JobSiteCode, IReadOnlyList<DeliveryReceivedLine> Lines);

public sealed record DeliveryReceivedLine(int LineNumber, string ItemSku, decimal Quantity);
