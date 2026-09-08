namespace Stockyard.Infrastructure.Procedures;

/// <summary>One row of the dbo.ReceiptLineType table-valued parameter.</summary>
public sealed record ReceiptLine(int LineNumber, decimal Quantity);
