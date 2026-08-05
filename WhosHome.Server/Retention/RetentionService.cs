using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhosHome.Server.Configuration;
using WhosHome.Server.Data;

namespace WhosHome.Server.Retention;

/// <summary>
/// Deletes derived reports once they are old enough to be uninteresting. Raw coordinates need
/// no sweeping: there is only ever one per person and it is overwritten in place on ingest.
/// </summary>
public class RetentionService(
    IServiceScopeFactory scopeFactory,
    IOptions<WhosHomeOptions> options,
    TimeProvider timeProvider,
    ILogger<RetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);

    private readonly WhosHomeOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(SweepInterval, timeProvider);

        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A failed sweep must not take the server down; the next one will retry.
                logger.LogError(exception, "Retention sweep failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        WhosHomeContext context = scope.ServiceProvider.GetRequiredService<WhosHomeContext>();

        DateTimeOffset cutoff = timeProvider.GetUtcNow() - _options.ReportRetention;

        int deleted = await context.Reports
            .Where(report => report.ReceivedUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            logger.LogInformation("Retention sweep deleted {Deleted} reports.", deleted);
        }
    }
}
