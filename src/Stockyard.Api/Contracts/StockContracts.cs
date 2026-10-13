namespace Stockyard.Api.Contracts;

public sealed record IssueStockRequest(string? ItemSku, string? JobSiteCode, decimal Quantity, string? Reference);

/// <summary>The Issue movement; <see cref="Id"/> is the ledger row id.</summary>
public sealed record StockIssueResponse(long Id, string ItemSku, string JobSiteCode, decimal Quantity, string Reference, DateTime OccurredAt);

public sealed record StockOnHandResponse(string ItemSku, string JobSiteCode, decimal Quantity, DateTime LastMovementAt);
