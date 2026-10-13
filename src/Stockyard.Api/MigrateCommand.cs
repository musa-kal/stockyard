using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Stockyard.Infrastructure;
using Stockyard.Infrastructure.Persistence;

namespace Stockyard.Api;

/// <summary>
/// "dotnet Stockyard.Api.dll migrate" applies the EF Core migrations (tables, then the SQL objects)
/// and exits. It waits for SQL Server to accept connections, so a container can start it as soon
/// as the database container is up.
/// </summary>
internal static partial class MigrateCommand
{
    public const string Name = "migrate";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    private const int MaxAttempts = 20;

    public static async Task<int> RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddStockyardDatabase(builder.Configuration);
        using var host = builder.Build();

        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MigrateCommand));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = host.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<StockyardDbContext>();
                var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
                await db.Database.MigrateAsync();
                if (pending.Length == 0)
                {
                    LogUpToDate(logger);
                }
                else
                {
                    LogApplied(logger, pending.Length, pending);
                }

                return 0;
            }
            catch (SqlException ex) when (attempt < MaxAttempts)
            {
                LogWaiting(logger, attempt, MaxAttempts, ex.Message);
                await Task.Delay(RetryDelay);
            }
            catch (Exception ex)
            {
                LogFailed(logger, ex);
                return 1;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied {Count} migrations: {Migrations}")]
    private static partial void LogApplied(ILogger logger, int count, string[] migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "The database is up to date; no migration was pending")]
    private static partial void LogUpToDate(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SQL Server not ready (attempt {Attempt} of {MaxAttempts}): {Reason}")]
    private static partial void LogWaiting(ILogger logger, int attempt, int maxAttempts, string reason);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Migration failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
