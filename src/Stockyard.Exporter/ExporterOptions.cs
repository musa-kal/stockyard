namespace Stockyard.Exporter;

/// <summary>Bound from the "Exporter" section (Exporter__Destination, Exporter__Cron, Exporter__ServiceUrl).</summary>
public sealed class ExporterOptions
{
    public const string SectionName = "Exporter";

    /// <summary>A folder, or s3://bucket/prefix.</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>Five-field cron expression, evaluated in UTC.</summary>
    public string Cron { get; set; } = "0 2 * * *";

    /// <summary>S3 endpoint override for LocalStack. Empty on AWS.</summary>
    public string ServiceUrl { get; set; } = string.Empty;
}
