using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Stockyard.Domain;
using Stockyard.Domain.Events;
using Stockyard.Infrastructure.Persistence;

namespace Stockyard.Infrastructure.Procedures;

/// <summary>
/// Calls the hand-written procedures. Business errors raised with THROW 50000 to 50005 come back
/// as <see cref="DomainException"/> carrying the same number; anything else stays a SqlException.
/// </summary>
public sealed class StockyardProcedures(StockyardDbContext db)
{
    public Task PostPurchaseOrderAsync(Guid purchaseOrderId, string idempotencyKey, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "EXEC dbo.usp_PostPurchaseOrder @PurchaseOrderId = @PurchaseOrderId, @IdempotencyKey = @IdempotencyKey",
            cancellationToken,
            new SqlParameter("@PurchaseOrderId", SqlDbType.UniqueIdentifier) { Value = purchaseOrderId },
            new SqlParameter("@IdempotencyKey", SqlDbType.NVarChar, 64) { Value = idempotencyKey });
    }

    public async Task ReceiveDeliveryAsync(Guid purchaseOrderId, IEnumerable<ReceiptLine> lines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);

        using var table = new DataTable();
        table.Locale = CultureInfo.InvariantCulture;
        table.Columns.Add("LineNumber", typeof(int));
        table.Columns.Add("Quantity", typeof(decimal));
        foreach (var line in lines)
        {
            table.Rows.Add(line.LineNumber, line.Quantity);
        }

        await ExecuteAsync(
            "EXEC dbo.usp_ReceiveDelivery @PurchaseOrderId = @PurchaseOrderId, @Lines = @Lines",
            cancellationToken,
            new SqlParameter("@PurchaseOrderId", SqlDbType.UniqueIdentifier) { Value = purchaseOrderId },
            new SqlParameter("@Lines", SqlDbType.Structured) { TypeName = "dbo.ReceiptLineType", Value = table });
    }

    public Task IssueStockAsync(Guid itemId, Guid jobSiteId, decimal quantity, string reference, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            "EXEC dbo.usp_IssueStock @ItemId = @ItemId, @JobSiteId = @JobSiteId, @Quantity = @Quantity, @Reference = @Reference",
            cancellationToken,
            new SqlParameter("@ItemId", SqlDbType.UniqueIdentifier) { Value = itemId },
            new SqlParameter("@JobSiteId", SqlDbType.UniqueIdentifier) { Value = jobSiteId },
            new SqlParameter("@Quantity", SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = quantity },
            new SqlParameter("@Reference", SqlDbType.NVarChar, 40) { Value = reference });
    }

    /// <summary>
    /// Runs the reconcile. With <paramref name="apply"/> the projection is corrected and a
    /// StockReconciled outbox row is written when anything differed; without it nothing changes.
    /// </summary>
    public async Task<IReadOnlyList<StockDifference>> ReconcileStockAsync(bool apply, CancellationToken cancellationToken)
    {
        var rows = await db.Database
            .SqlQueryRaw<DifferenceRow>(
                "EXEC dbo.usp_ReconcileStock @Apply = @Apply",
                new SqlParameter("@Apply", SqlDbType.Bit) { Value = apply })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new StockDifference(r.ItemSku, r.JobSiteCode, r.Projected, r.Actual))];
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken, params SqlParameter[] parameters)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
        }
        catch (SqlException ex) when (IsBusinessError(ex.Number))
        {
            throw new DomainException(ex.Number, ex.Message, ex);
        }
    }

    private static bool IsBusinessError(int number) =>
        number is >= ErrorNumbers.Validation and <= ErrorNumbers.InvalidState;

    private sealed class DifferenceRow
    {
        public required string ItemSku { get; init; }

        public required string JobSiteCode { get; init; }

        public decimal Projected { get; init; }

        public decimal Actual { get; init; }
    }
}
