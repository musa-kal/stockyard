namespace Stockyard.Domain.Events;

/// <summary>Informational: the reconcile already corrected these rows when this was written.</summary>
public sealed record StockReconciled(IReadOnlyList<StockDifference> Differences);

public sealed record StockDifference(string ItemSku, string JobSiteCode, decimal Projected, decimal Actual);
