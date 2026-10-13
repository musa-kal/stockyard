using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stockyard.Infrastructure.Outbox;
using Stockyard.Infrastructure.Persistence;
using Stockyard.Infrastructure.Procedures;
using Stockyard.Infrastructure.Queries;

namespace Stockyard.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Stockyard";

    /// <summary>Registers the DbContext on ConnectionStrings__Stockyard and the data access services.</summary>
    public static IServiceCollection AddStockyardDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"ConnectionStrings__{ConnectionStringName} is not set.");
        }

        services.AddDbContext<StockyardDbContext>(options => options.UseSqlServer(connectionString));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<StockyardProcedures>();
        services.AddScoped<StockQueries>();
        services.AddScoped<OutboxStore>();
        return services;
    }
}
