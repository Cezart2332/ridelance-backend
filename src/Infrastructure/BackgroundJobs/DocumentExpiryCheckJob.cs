using Application.Abstractions.Messaging;
using Application.Notifications.DocumentExpiry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Runs daily at 08:00 Romania time.
/// Sends push + in-app notifications for documents expiring in 30 or 7 days.
/// </summary>
internal sealed class DocumentExpiryCheckJob(
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentExpiryCheckJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DocumentExpiryCheckJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (IsEightAmRomania())
                {
                    logger.LogInformation("Running daily document expiry check...");

                    using IServiceScope scope = scopeFactory.CreateScope();
                    ICommandHandler<CheckDocumentExpiryCommand> handler =
                        scope.ServiceProvider.GetRequiredService<ICommandHandler<CheckDocumentExpiryCommand>>();

                    Result result = await handler.Handle(
                        new CheckDocumentExpiryCommand(),
                        stoppingToken);

                    if (result.IsFailure)
                    {
                        logger.LogWarning("Document expiry check returned failure: {Error}", result.Error);
                    }

                    // Sleep 1 hour to avoid re-triggering within the same hour
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in DocumentExpiryCheckJob.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private static bool IsEightAmRomania()
    {
        var romania = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        DateTime nowRomania = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, romania);

        return nowRomania.Hour == 8 && nowRomania.Minute == 0;
    }
}
