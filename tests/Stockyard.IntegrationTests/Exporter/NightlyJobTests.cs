using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Parquet;
using Parquet.Serialization;
using Stockyard.Domain;
using Stockyard.Exporter;
using Stockyard.Infrastructure.Persistence;
using Stockyard.Infrastructure.Procedures;

namespace Stockyard.IntegrationTests.Exporter;

public sealed class NightlyJobTests(SqlServerFixture sql, LocalStackFixture localStack) : IClassFixture<LocalStackFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task TheExportWritesTheDayInTheWarehouseLayoutWithTheContractColumns()
    {
        var (order, item) = await ReceivedOrderAsync();
        var folder = Path.Combine(Path.GetTempPath(), "stockyard-export-tests", Guid.NewGuid().ToString("N"));

        await using var db = sql.CreateDbContext();
        var result = await Job(db, new FileSystemDestination(folder)).RunAsync(Today, Ct);

        result.Files.Count.ShouldBe(2);
        var ordersFile = Path.Combine(folder, ExportPaths.OrderLines(Today));
        var movementsFile = Path.Combine(folder, ExportPaths.Movements(Today));
        File.Exists(ordersFile).ShouldBeTrue();
        File.Exists(movementsFile).ShouldBeTrue();

        (await ColumnsAsync(ordersFile)).ShouldBe(
        [
            "order_id", "order_number", "supplier_code", "job_site_code", "posted_at", "line_number",
            "item_sku", "quantity_ordered", "quantity_received", "unit_cost", "line_total",
        ]);
        (await ColumnsAsync(movementsFile)).ShouldBe(
            ["movement_id", "item_sku", "job_site_code", "kind", "quantity", "reference", "occurred_at"]);

        var lines = (await ParquetSerializer.DeserializeAsync<OrderLineRecord>(ordersFile, cancellationToken: Ct)).Data;
        var line = lines.Single(l => l.OrderNumber == order.Number);
        line.OrderId.ShouldBe(order.Id.ToString());
        line.ItemSku.ShouldBe(item.Sku);
        line.QuantityReceived.ShouldBe(6d);
        line.LineTotal.ShouldBe(6d * (double)item.UnitCost);

        var movements = (await ParquetSerializer.DeserializeAsync<StockMovementRecord>(movementsFile, cancellationToken: Ct)).Data;
        var movement = movements.Single(m => m.Reference == order.Number);
        movement.JobSiteCode.ShouldBe(JobSite.WarehouseCode);
        movement.Kind.ShouldBe("Receipt");
        movement.Quantity.ShouldBe(6d);
    }

    [Fact]
    public async Task ADayWithoutActivityStillGetsBothFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "stockyard-export-tests", Guid.NewGuid().ToString("N"));
        var quietDay = new DateOnly(2001, 1, 1);

        await using var db = sql.CreateDbContext();
        var result = await Job(db, new FileSystemDestination(folder)).RunAsync(quietDay, Ct);

        result.OrderLines.ShouldBe(0);
        result.Movements.ShouldBe(0);
        File.Exists(Path.Combine(folder, ExportPaths.OrderLines(quietDay))).ShouldBeTrue();
    }

    [Fact]
    public async Task TheExportCanBeWrittenToS3()
    {
        await ReceivedOrderAsync();
        using var s3 = new AmazonS3Client(
            LocalStackFixture.Credentials,
            new AmazonS3Config { ServiceURL = localStack.ServiceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        await s3.PutBucketAsync("stockyard-exports", Ct);
        var location = new Stockyard.Exporter.S3Location("stockyard-exports", "exports");

        await using var db = sql.CreateDbContext();
        var result = await Job(db, new S3Destination(s3, location)).RunAsync(Today, Ct);

        result.Files.ShouldContain($"s3://stockyard-exports/exports/{ExportPaths.Movements(Today)}");
        var listed = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = "stockyard-exports", Prefix = "exports/" }, Ct);
        listed.S3Objects.Select(o => o.Key).ShouldContain($"exports/{ExportPaths.OrderLines(Today)}");
    }

    private static NightlyJob Job(StockyardDbContext db, IExportDestination destination) =>
        new(new StockyardProcedures(db), new ExportQueries(db), destination, NullLogger<NightlyJob>.Instance);

    private async Task<(PurchaseOrder Order, Item Item)> ReceivedOrderAsync()
    {
        await using var db = sql.CreateDbContext();
        var item = await TestData.AddItemAsync(db, unitCost: 2.25m);
        var order = await TestData.ReceiveStockAsync(db, item, JobSite.WarehouseId, 6);
        return (order, item);
    }

    private static async Task<string[]> ColumnsAsync(string path)
    {
        await using var reader = await ParquetReader.CreateAsync(path, cancellationToken: Ct);
        return [.. reader.Schema.GetDataFields().Select(f => f.Name)];
    }
}
