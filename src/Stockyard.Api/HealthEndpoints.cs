using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Stockyard.Infrastructure;

namespace Stockyard.Api;

public static class HealthEndpoints
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddStockyardHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddSqlServer(
                configuration.GetConnectionString(DependencyInjection.ConnectionStringName)!,
                name: "sqlserver",
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(3));
        return services;
    }

    /// <summary>/healthz has no dependencies (liveness); /readyz checks SQL Server (readiness).</summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) });
        return app;
    }
}
