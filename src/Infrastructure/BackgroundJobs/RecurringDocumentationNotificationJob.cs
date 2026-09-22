using Application.Abstractions.Messaging;
using Application.Accounting;
using Application.Notifications.RecurringDocumentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Trimite cererea de documente lunare tuturor clienților în ziua în care se deschide fereastra
/// lunii contabile — pe 26, la 08:00, ora României (vezi <see cref="AccountingPeriod"/>).
/// </summary>
internal sealed class RecurringDocumentationNotificationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<RecurringDocumentationNotificationJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RecurringDocumentationNotificationJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (IsCollectionStartEightAmRomania())
                {
                    logger.LogInformation("Sending monthly recurring documentation notifications...");

                    using IServiceScope scope = scopeFactory.CreateScope();
                    ICommandHandler<SendRecurringDocumentationNotificationsCommand, SendRecurringDocumentationNotificationsResult> handler =
                        scope.ServiceProvider.GetRequiredService<ICommandHandler<SendRecurringDocumentationNotificationsCommand, SendRecurringDocumentationNotificationsResult>>();

                    Result<SendRecurringDocumentationNotificationsResult> result = await handler.Handle(
                        new SendRecurringDocumentationNotificationsCommand(
                            TargetUserId: null,
                            RequireCollectionStartDay: true,
                            ForceResend: false),
                        stoppingToken);

                    if (result.IsSuccess)
                    {
                        logger.LogInformation(
                            "Recurring documentation notifications sent. Users={Users}, InApp={InApp}, Push={Push}",
                            result.Value.UsersNotified,
                            result.Value.InAppCreated,
                            result.Value.PushSent);
                    }

                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in RecurringDocumentationNotificationJob.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private static bool IsCollectionStartEightAmRomania()
    {
        DateTime nowRomania = AccountingPeriod.ToRomania(DateTime.UtcNow);

        return AccountingPeriod.IsCollectionStartDay(DateOnly.FromDateTime(nowRomania)) &&
               nowRomania.Hour == 8 &&
               nowRomania.Minute == 0;
    }
}
