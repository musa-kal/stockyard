using Amazon.S3;
using Cronos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Stockyard.Exporter;

public static class DependencyInjection
{
    /// <summary>Registers the reconcile and export job. The api calls this too, for on-demand runs.</summary>
    public static IServiceCollection AddStockyardExports(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ExporterOptions>()
            .Bind(configuration.GetSection(ExporterOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Destination), "Exporter__Destination is required (a folder or s3://bucket/prefix).")
            .Validate(o => TryParseCron(o.Cron), "Exporter__Cron is not a valid five-field cron expression.")
            .ValidateOnStart();

        services.AddSingleton<IAmazonS3>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ExporterOptions>>().Value;
            var config = new AmazonS3Config();
            if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
            {
                // LocalStack serves buckets by path, not by virtual host.
                config.ServiceURL = options.ServiceUrl;
                config.ForcePathStyle = true;
            }

            return new AmazonS3Client(config);
        });

        services.AddSingleton<IExportDestination>(sp =>
        {
            var destination = sp.GetRequiredService<IOptions<ExporterOptions>>().Value.Destination;
            return S3Location.TryParse(destination, out var location)
                ? new S3Destination(sp.GetRequiredService<IAmazonS3>(), location)
                : new FileSystemDestination(destination);
        });

        services.AddScoped<ExportQueries>();
        services.AddScoped<NightlyJob>();
        return services;
    }

    private static bool TryParseCron(string expression)
    {
        try
        {
            CronExpression.Parse(expression);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }
}
