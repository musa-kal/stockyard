using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Stockyard.Infrastructure.Persistence;

/// <summary>
/// Lets "dotnet ef" build the model without starting a host. Adding a migration never opens a
/// connection; "dotnet ef database update" reads ConnectionStrings__Stockyard.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StockyardDbContext>
{
    public StockyardDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Stockyard")
            ?? "Server=localhost,1433;Database=Stockyard;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<StockyardDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new StockyardDbContext(options);
    }
}
