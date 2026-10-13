using Stockyard.Infrastructure;

namespace Stockyard.Exporter;

// An explicit entry point: the api references this project for the job, and a top-level
// Program here would collide with the api's own Program class.
public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

        builder.Services.AddStockyardDatabase(builder.Configuration);
        builder.Services.AddStockyardExports(builder.Configuration);
        builder.Services.AddHostedService<ExporterService>();

        await builder.Build().RunAsync();
    }
}
