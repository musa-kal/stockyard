using Cronos;
using Microsoft.Extensions.Options;

namespace Stockyard.Exporter;

/// <summary>
/// Runs the nightly job on Exporter__Cron (UTC). Each run exports the UTC day before the
/// scheduled time, so the default 02:00 run picks up yesterday after it has closed.
/// </summary>
public sealed partial class ExporterService(
    IServiceScopeFactory scopeFactory,
    IOptions<ExporterOptions> options,
    TimeProvider timeProvider,
    ILogger<ExporterService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var schedule = CronExpression.Parse(options.Value.Cron);
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            var next = schedule.GetNextOccurrence(now, TimeZoneInfo.Utc);
            if (next is null)
            {
                return;
            }

            LogNextRun(next.Value);
            try
            {
                await Task.Delay(next.Value - now, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var day = DayBefore(next.Value);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NightlyJob>().RunAsync(day, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogRunFailed(day, ex);
            }
        }
    }

    /// <summary>The UTC day the run at <paramref name="scheduledAt"/> exports.</summary>
    public static DateOnly DayBefore(DateTimeOffset scheduledAt) =>
        DateOnly.FromDateTime(scheduledAt.UtcDateTime).AddDays(-1);

    [LoggerMessage(Level = LogLevel.Information, Message = "Next reconcile and export at {NextRun:u}")]
    private partial void LogNextRun(DateTimeOffset nextRun);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconcile and export for {Day} failed; the next scheduled run will try the following day")]
    private partial void LogRunFailed(DateOnly day, Exception exception);
}
