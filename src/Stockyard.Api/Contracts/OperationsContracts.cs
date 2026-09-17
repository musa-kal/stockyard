using Stockyard.Domain.Events;

namespace Stockyard.Api.Contracts;

/// <summary>
/// The ledger compared with the projection, read only. Pairs whose events are still on the way
/// are not reported; <see cref="PendingMessages"/> and <see cref="UnpublishedOutboxMessages"/> say
/// how much is still on the way.
/// </summary>
public sealed record ReconcileReport(
    IReadOnlyList<StockDifference> Differences,
    long PendingMessages,
    int UnpublishedOutboxMessages,
    DateTime CheckedAt);

public sealed record WebhookReplayResponse(Guid Id, DateTime NextAttemptAt);

public sealed record DeadLetterReplayResponse(int Replayed);
