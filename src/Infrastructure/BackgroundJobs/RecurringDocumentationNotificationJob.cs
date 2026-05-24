using Application.Abstractions.Messaging;
using Application.Notifications.RecurringDocumentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Sends recurring-documentation reminders to all clients on the 1st of each month at 08:00 Romania time.
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
                if (IsFirstOfMonthEightAmRomania())
                {
                    logger.LogInformation("Sending monthly recurring documentation notifications...");

                    using IServiceScope scope = scopeFactory.CreateScope();
                    ICommandHandler<SendRecurringDocumentationNotificationsCommand, SendRecurringDocumentationNotificationsResult> handler =
                        scope.ServiceProvider.GetRequiredService<ICommandHandler<SendRecurringDocumentationNotificationsCommand, SendRecurringDocumentationNotificationsResult>>();

                    Result<SendRecurringDocumentationNotificationsResult> result = await handler.Handle(
                        new SendRecurringDocumentationNotificationsCommand(
                            TargetUserId: null,
                            RequireFirstOfMonth: true,
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

    private static bool IsFirstOfMonthEightAmRomania()
    {
        var romania = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        DateTime nowRomania = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, romania);

        return nowRomania.Day == 1 &&
               nowRomania.Hour == 8 &&
               nowRomania.Minute == 0;
    }
}
