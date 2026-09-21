using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Stockyard.IntegrationTests.Api;

/// <summary>The real api on the test SQL Server, with the in-memory bus instead of SQS.</summary>
public sealed class ApiFactory(SqlServerFixture sql) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Stockyard", sql.ConnectionString);
        builder.UseSetting("Messaging:Provider", "InMemory");
        builder.UseSetting("Outbox:PollInterval", "00:00:00.200");
    }
}
