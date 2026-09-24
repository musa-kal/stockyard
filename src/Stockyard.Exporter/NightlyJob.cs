using Microsoft.Extensions.Logging;
using Parquet.Serialization;
using Stockyard.Infrastructure.Procedures;

namespace Stockyard.Exporter;

public sealed record ExportResult(DateOnly Date, int Differences, int OrderLines, int Movements, IReadOnlyList<string> Files);

/// <summary>
/// The reconcile and export run: corrects the projection from the ledger (the procedure writes
/// the StockReconciled outbox row when anything differed), then writes one UTC day of order lines
/// and movements as Parquet. Scheduled by the exporter worker and run on demand by the api.
/// </summary>
public sealed partial class NightlyJob(
    StockyardProcedures procedures,
    ExportQueries queries,
    IExportDestination destination,
    ILogger<NightlyJob> logger)
{
    public async Task<ExportResult> RunAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var differences = await procedures.ReconcileStockAsync(apply: true, cancellationToken);
        if (differences.Count > 0)
        {
            LogCorrected(differences.Count);
        }

        var orderLines = await queries.OrderLinesAsync(day, cancellationToken);
        var movements = await queries.MovementsAsync(day, cancellationToken);

        var files = new List<string>
        {
            await WriteAsync(orderLines, ExportPaths.OrderLines(day), cancellationToken),
            await WriteAsync(movements, ExportPaths.Movements(day), cancellationToken),
        };

        LogExported(day, orderLines.Count, movements.Count);
        return new ExportResult(day, differences.Count, orderLines.Count, movements.Count, files);
    }

    private async Task<string> WriteAsync<T>(IReadOnlyCollection<T> rows, string relativePath, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await ParquetSerializer.SerializeAsync(rows, buffer, cancellationToken: cancellationToken);
        buffer.Position = 0;
        return await destination.WriteAsync(relativePath, buffer, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reconcile corrected {Count} stock on hand rows")]
    private partial void LogCorrected(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Exported {Day}: {OrderLines} order lines, {Movements} movements")]
    private partial void LogExported(DateOnly day, int orderLines, int movements);
}
