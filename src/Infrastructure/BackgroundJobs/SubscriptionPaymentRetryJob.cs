using Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Rulează reîncercările de încasare a abonamentelor (<see cref="SubscriptionPaymentRetryRunner"/>).
/// La 10 minute: reîncercarea cade la cel mult 10 minute după ora încasării care a eșuat.
/// </summary>
internal sealed class SubscriptionPaymentRetryJob(
    IServiceScopeFactory scopeFactory,
    ILogger<SubscriptionPaymentRetryJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                SubscriptionPaymentRetryRunner runner = scope.ServiceProvider.GetRequiredService<SubscriptionPaymentRetryRunner>();
                int retried = await runner.RunDueAsync(DateTime.UtcNow, stoppingToken);
                if (retried > 0)
                {
                    logger.LogInformation("Reîncercări de încasare rulate: {Count}.", retried);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "SubscriptionPaymentRetryJob a eșuat.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}
