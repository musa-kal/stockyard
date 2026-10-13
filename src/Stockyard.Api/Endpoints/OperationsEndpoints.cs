using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Stockyard.Api.Contracts;
using Stockyard.Domain;
using Stockyard.Exporter;
using Stockyard.Infrastructure.Outbox;
using Stockyard.Infrastructure.Persistence;
using Stockyard.Infrastructure.Procedures;
using Stockyard.Messaging;

namespace Stockyard.Api.Endpoints;

/// <summary>Endpoints for the people running Stockyard rather than the office using it.</summary>
public static class OperationsEndpoints
{
    private const int DefaultReplayLimit = 100;

    public static RouteGroupBuilder MapOperationsEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet("/stock/reconcile", ReconcileAsync).WithTags("Stock");
        api.MapPost("/webhooks/{deliveryId:guid}/replay", ReplayWebhookAsync).WithTags("Operations");
        api.MapPost("/outbox/dlq/replay", ReplayDeadLettersAsync).WithTags("Operations");
        api.MapPost("/exports/run", RunExportAsync).WithTags("Operations");

        return api;
    }

    /// <summary>Read only: runs the reconcile comparison without correcting anything.</summary>
    private static async Task<Ok<ReconcileReport>> ReconcileAsync(
        StockyardProcedures procedures,
        OutboxStore outbox,
        IMessageBus bus,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var differences = await procedures.ReconcileStockAsync(apply: false, cancellationToken);
        var unpublished = await outbox.CountUnpublishedAsync(cancellationToken);
        var pending = await bus.GetPendingCountAsync(cancellationToken);

        return TypedResults.Ok(new ReconcileReport(differences, pending, unpublished, timeProvider.GetUtcNow().UtcDateTime));
    }

    /// <summary>Queues a webhook delivery again, whether it was delivered, failing or abandoned.</summary>
    private static async Task<Accepted<WebhookReplayResponse>> ReplayWebhookAsync(
        Guid deliveryId,
        StockyardDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var updated = await db.WebhookDeliveries
            .Where(w => w.Id == deliveryId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(w => w.Attempts, 0)
                    .SetProperty(w => w.NextAttemptAt, now)
                    .SetProperty(w => w.DeliveredAt, (DateTime?)null)
                    .SetProperty(w => w.LastStatusCode, (int?)null)
                    .SetProperty(w => w.LastError, (string?)null),
                cancellationToken);

        if (updated == 0)
        {
            throw new DomainException(ErrorNumbers.NotFound, "Webhook delivery not found.");
        }

        return TypedResults.Accepted((string?)null, new WebhookReplayResponse(deliveryId, now));
    }

    /// <summary>Moves dead letters back to the events queue; the consumer's guard makes this safe.</summary>
    private static async Task<Ok<DeadLetterReplayResponse>> ReplayDeadLettersAsync(
        int? max,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var limit = max ?? DefaultReplayLimit;
        if (limit is < 1 or > 1000)
        {
            throw new DomainException("max must be between 1 and 1000.");
        }

        var replayed = await bus.ReplayDeadLettersAsync(limit, cancellationToken);
        return TypedResults.Ok(new DeadLetterReplayResponse(replayed));
    }

    /// <summary>Runs the exporter's reconcile and export now, for one UTC day (yesterday by default).</summary>
    private static async Task<Ok<ExportResult>> RunExportAsync(
        DateOnly? date,
        NightlyJob job,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var day = date ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(-1);
        var result = await job.RunAsync(day, cancellationToken);
        return TypedResults.Ok(result);
    }
}
