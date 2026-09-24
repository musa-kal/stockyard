using Amazon.S3;
using Amazon.S3.Model;

namespace Stockyard.Exporter;

public sealed class S3Destination(IAmazonS3 s3, S3Location location) : IExportDestination
{
    public const string ParquetContentType = "application/vnd.apache.parquet";

    public async Task<string> WriteAsync(string relativePath, Stream content, CancellationToken cancellationToken)
    {
        var key = location.KeyFor(relativePath);
        await s3.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = location.Bucket,
                Key = key,
                InputStream = content,
                ContentType = ParquetContentType,
                AutoCloseStream = false,
            },
            cancellationToken);
        return $"{S3Location.Scheme}{location.Bucket}/{key}";
    }
}
