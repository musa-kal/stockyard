namespace Stockyard.Infrastructure.Procedures;

/// <summary>The Issue movement written by dbo.usp_IssueStock.</summary>
public sealed record IssuedStock(long MovementId, DateTime OccurredAt);
