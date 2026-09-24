using Stockyard.Exporter;

namespace Stockyard.UnitTests.Exporter;

public sealed class ExportLayoutTests
{
    [Fact]
    public void PathsFollowTheWarehouseLayout()
    {
        var day = new DateOnly(2026, 9, 7);

        ExportPaths.OrderLines(day).ShouldBe("orders/2026/09/07/order_lines.parquet");
        ExportPaths.Movements(day).ShouldBe("movements/2026/09/07/stock_movements.parquet");
    }

    [Theory]
    [InlineData("s3://stockyard-exports/exports", "stockyard-exports", "exports")]
    [InlineData("s3://stockyard-exports/exports/", "stockyard-exports", "exports")]
    [InlineData("S3://bucket", "bucket", "")]
    [InlineData("s3://bucket/a/b", "bucket", "a/b")]
    public void S3DestinationsAreSplitIntoBucketAndPrefix(string destination, string bucket, string prefix)
    {
        S3Location.TryParse(destination, out var location).ShouldBeTrue();

        location.ShouldBe(new S3Location(bucket, prefix));
    }

    [Theory]
    [InlineData("/var/exports")]
    [InlineData("exports")]
    [InlineData("s3://")]
    [InlineData(null)]
    public void FoldersAndBrokenUrisAreNotS3(string? destination)
    {
        S3Location.TryParse(destination, out _).ShouldBeFalse();
    }

    [Fact]
    public void KeysArePrefixed()
    {
        new S3Location("b", "exports").KeyFor("orders/2026/09/07/order_lines.parquet")
            .ShouldBe("exports/orders/2026/09/07/order_lines.parquet");
        new S3Location("b", string.Empty).KeyFor("x.parquet").ShouldBe("x.parquet");
    }

    [Theory]
    [InlineData("2026-09-08T02:00:00Z", "2026-09-07")]
    [InlineData("2026-10-01T00:30:00Z", "2026-09-30")]
    public void AScheduledRunExportsThePreviousUtcDay(string scheduledAt, string expectedDay)
    {
        ExporterService.DayBefore(DateTimeOffset.Parse(scheduledAt, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(DateOnly.Parse(expectedDay, System.Globalization.CultureInfo.InvariantCulture));
    }
}
