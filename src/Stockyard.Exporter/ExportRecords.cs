using System.Text.Json.Serialization;
using Parquet.Serialization.Attributes;

namespace Stockyard.Exporter;

/// <summary>One row of order_lines.parquet. Column names are the warehouse layer's contract.</summary>
public sealed class OrderLineRecord
{
    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("order_number")]
    public string OrderNumber { get; set; } = string.Empty;

    [JsonPropertyName("supplier_code")]
    public string SupplierCode { get; set; } = string.Empty;

    [JsonPropertyName("job_site_code")]
    public string JobSiteCode { get; set; } = string.Empty;

    [JsonPropertyName("posted_at")]
    [ParquetTimestamp(ParquetTimestampResolution.Milliseconds, useLogicalTimestamp: true, isAdjustedToUTC: true)]
    public DateTime PostedAt { get; set; }

    [JsonPropertyName("line_number")]
    public int LineNumber { get; set; }

    [JsonPropertyName("item_sku")]
    public string ItemSku { get; set; } = string.Empty;

    [JsonPropertyName("quantity_ordered")]
    public double QuantityOrdered { get; set; }

    [JsonPropertyName("quantity_received")]
    public double QuantityReceived { get; set; }

    [JsonPropertyName("unit_cost")]
    public double UnitCost { get; set; }

    [JsonPropertyName("line_total")]
    public double LineTotal { get; set; }
}

/// <summary>One row of stock_movements.parquet.</summary>
public sealed class StockMovementRecord
{
    [JsonPropertyName("movement_id")]
    public long MovementId { get; set; }

    [JsonPropertyName("item_sku")]
    public string ItemSku { get; set; } = string.Empty;

    /// <summary>"WAREHOUSE" for movements held off site.</summary>
    [JsonPropertyName("job_site_code")]
    public string JobSiteCode { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public double Quantity { get; set; }

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("occurred_at")]
    [ParquetTimestamp(ParquetTimestampResolution.Milliseconds, useLogicalTimestamp: true, isAdjustedToUTC: true)]
    public DateTime OccurredAt { get; set; }
}
