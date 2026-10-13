using System.Globalization;

namespace Stockyard.Exporter;

/// <summary>Where the files go: a folder, or an S3 bucket and prefix.</summary>
public interface IExportDestination
{
    /// <summary>Writes a file at <paramref name="relativePath"/> and returns where it ended up.</summary>
    Task<string> WriteAsync(string relativePath, Stream content, CancellationToken cancellationToken);
}

/// <summary>The layout the warehouse layer reads: kind/yyyy/MM/dd/file.parquet.</summary>
public static class ExportPaths
{
    public static string OrderLines(DateOnly day) => $"orders/{Day(day)}/order_lines.parquet";

    public static string Movements(DateOnly day) => $"movements/{Day(day)}/stock_movements.parquet";

    private static string Day(DateOnly day) => day.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
}

/// <summary>An s3://bucket/prefix destination split into its parts.</summary>
public sealed record S3Location(string Bucket, string Prefix)
{
    public const string Scheme = "s3://";

    public static bool TryParse(string? destination, out S3Location location)
    {
        location = new S3Location(string.Empty, string.Empty);
        if (destination is null || !destination.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = destination[Scheme.Length..].Trim('/');
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        var bucket = slash < 0 ? rest : rest[..slash];
        var prefix = slash < 0 ? string.Empty : rest[(slash + 1)..].Trim('/');
        if (bucket.Length == 0)
        {
            return false;
        }

        location = new S3Location(bucket, prefix);
        return true;
    }

    public string KeyFor(string relativePath) => Prefix.Length == 0 ? relativePath : $"{Prefix}/{relativePath}";
}
