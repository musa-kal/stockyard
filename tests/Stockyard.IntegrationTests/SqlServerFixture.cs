using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Stockyard.Infrastructure.Persistence;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(Stockyard.IntegrationTests.SqlServerFixture))]

// One SQL Server container serves the whole assembly; running classes one at a time keeps the
// machine's memory in check and keeps the reconcile tests away from half-finished writes.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace Stockyard.IntegrationTests;

/// <summary>Starts SQL Server 2022 once and applies the real migrations, procedures included.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public const string Image = "mcr.microsoft.com/mssql/server:2022-latest";

    private readonly MsSqlContainer container = new MsSqlBuilder(Image).Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync();

        ConnectionString = new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = "Stockyard",
        }.ConnectionString;

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public StockyardDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<StockyardDbContext>().UseSqlServer(ConnectionString).Options);

    public ValueTask DisposeAsync() => container.DisposeAsync();
}
