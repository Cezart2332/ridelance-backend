using Application.Abstractions.Messaging;
using Application.Accounting;
using Application.Notifications.RecurringDocumentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// Închiderea lunii, după 08:00 (ora României): pe 1 anunțul pentru luna trecută, pe 4 și 5
/// amintirea pentru cine n-a confirmat-o (vezi <see cref="AccountingPeriod"/>). Rula doar dacă
/// bucla nimerea exact minutul 08:00; acum rulează prima dată după 08:00, o dată pe zi.
/// Deduplicarea din handler ține oricum câte un anunț pe lună și o amintire pe zi.
/// </summary>
internal sealed class RecurringDocumentationNotificationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<RecurringDocumentationNotificationJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private const int RunFromHour = 8;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RecurringDocumentationNotificationJob started.");
        DateOnly? lastRun = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DateTime nowRomania = AccountingPeriod.ToRomania(DateTime.UtcNow);
                var today = DateOnly.FromDateTime(nowRomania);
                RecurringDocumentationKind? kind = KindFor(today);

                if (kind is not null && nowRomania.Hour >= RunFromHour && lastRun != today)
                {
                    logger.LogInformation("Sending monthly documents {Kind} notifications...", kind);

                    using IServiceScope scope = scopeFactory.CreateScope();
                    ICommandHandler<SendRecurringDocumentationNotificationsCommand, SendRecurringDocumentationNotificationsResult> handler =
                        scope.ServiceProvider.GetRequiredService<ICommandHandler<SendRecurringDocumentationNotificationsCommand, SendRecurringDocumentationNotificationsResult>>();

                    Result<SendRecurringDocumentationNotificationsResult> result = await handler.Handle(
                        new SendRecurringDocumentationNotificationsCommand(
                            TargetUserId: null,
                            RequireScheduledDay: true,
                            ForceResend: false,
                            Kind: kind.Value),
                        stoppingToken);

                    if (result.IsSuccess)
                    {
                        lastRun = today;
                        logger.LogInformation(
                            "Monthly documents notifications sent. Users={Users}, InApp={InApp}, Push={Push}",
                            result.Value.UsersNotified,
                            result.Value.InAppCreated,
                            result.Value.PushSent);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in RecurringDocumentationNotificationJob.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private static RecurringDocumentationKind? KindFor(DateOnly day)
    {
        if (day.Day == AccountingPeriod.NoticeDay)
        {
            return RecurringDocumentationKind.Notice;
        }

        return AccountingPeriod.ReminderDays.Contains(day.Day) ? RecurringDocumentationKind.Reminder : null;
    }
}
