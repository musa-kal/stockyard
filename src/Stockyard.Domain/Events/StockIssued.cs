namespace Stockyard.Domain.Events;

/// <summary>Stock left a site. Quantity is positive even though the ledger row is negative.</summary>
public sealed record StockIssued(string ItemSku, string JobSiteCode, decimal Quantity, string Reference);
