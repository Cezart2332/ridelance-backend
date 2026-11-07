using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Application.Accounting;
using Application.Notifications.TaxThreshold;
using Application.PfaRegistrations;
using Domain.Documents;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Notifications.RecurringDocumentation;

internal sealed class SendRecurringDocumentationNotificationsCommandHandler(
    IApplicationDbContext context,
    IWebPushService webPushService,
    IConfiguration configuration,
    FiscalEstimates.TaxYearParametersProvider taxParameters)
    : ICommandHandler<SendRecurringDocumentationNotificationsCommand, SendRecurringDocumentationNotificationsResult>
{
    public async Task<Result<SendRecurringDocumentationNotificationsResult>> Handle(
        SendRecurringDocumentationNotificationsCommand request,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(AccountingPeriod.ToRomania(nowUtc));
        bool reminder = request.Kind == RecurringDocumentationKind.Reminder;

        // Luna trecută se mai completează doar de pe 1 până pe 5; în rest nu e nimic de cerut.
        bool scheduledToday = reminder
            ? AccountingPeriod.ReminderDays.Contains(today.Day)
            : today.Day == AccountingPeriod.NoticeDay;
        if (!request.ForceResend && (!AccountingPeriod.IsClosingWindow(today) || request.RequireScheduledDay && !scheduledToday))
        {
            return new SendRecurringDocumentationNotificationsResult(0, 0, 0);
        }

        (int requestedYear, int requestedMonth) = AccountingPeriod.RequestedMonthUtc(nowUtc);
        (DateTime monthStartUtc, DateTime monthEndUtc) =
            AccountingPeriod.CollectionWindowUtc(requestedYear, requestedMonth);
        string monthKey = AccountingPeriod.MonthKey(requestedYear, requestedMonth);
        int taxYear = requestedYear;
        string notificationType = reminder ? NotificationTypes.RecurringDocumentationReminder : NotificationTypes.RecurringDocumentation;
        string notificationText = reminder ? RecurringDocumentationTexts.ReminderText : RecurringDocumentationTexts.BuildNotificationText(nowUtc);
        string pushTitle = reminder ? RecurringDocumentationTexts.ReminderPushTitle : RecurringDocumentationTexts.PushTitle;
        string pushBody = reminder ? RecurringDocumentationTexts.ReminderPushText : RecurringDocumentationTexts.BuildPushNotificationText(nowUtc);
        // Anunțul: unul pe lună. Amintirea: una pe zi (4 și 5).
        string dedupeKey = reminder
            ? $"monthly-docs-reminder:{monthKey}:{today.Day}"
            : $"monthly-docs:{monthKey}";
        Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase)
            ? parsedBase
            : null;

        IQueryable<User> usersQuery = context.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.Client);

        if (request.TargetUserId.HasValue)
        {
            usersQuery = usersQuery.Where(u => u.Id == request.TargetUserId.Value);
        }

        List<Guid> userIds = await usersQuery.Select(u => u.Id).ToListAsync(cancellationToken);

        if (userIds.Count == 0)
        {
            return new SendRecurringDocumentationNotificationsResult(0, 0, 0);
        }

        // Cine a confirmat luna („Nu am bonuri de încărcat”) nu mai primește nimic pentru ea.
        var confirmed = (await context.MonthlyDocumentsConfirmations
                .AsNoTracking()
                .Where(c => c.Year == requestedYear && c.Month == requestedMonth && userIds.Contains(c.UserId))
                .Select(c => c.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        int inAppCreated = 0;
        int pushSent = 0;
        int usersNotified = 0;
        int recurringDocumentationCreated = 0;
        int taxThresholdCreated = 0;

        foreach (Guid userId in userIds)
        {
            bool notifiedThisUser = false;
            List<PushSubscription>? subscriptions = null;

            bool documentationAlreadySent = confirmed.Contains(userId) || !request.ForceResend && await context.Notifications.AnyAsync(
                n => n.UserId == userId && n.DedupeKey == dedupeKey,
                cancellationToken);

            if (!documentationAlreadySent)
            {
                var notification = new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Text = notificationText,
                    Type = notificationType,
                    IsRead = false,
                    // Luna, pentru butonul „Nu am bonuri de încărcat” din notificare.
                    SectionKey = monthKey,
                    DedupeKey = request.ForceResend ? null : dedupeKey,
                    CreatedAtUtc = nowUtc,
                };

                context.Notifications.Add(notification);
                inAppCreated++;
                recurringDocumentationCreated++;
                notifiedThisUser = true;

                subscriptions = await GetPushSubscriptionsAsync(userId, cancellationToken);

                foreach (PushSubscription subscription in subscriptions)
                {
                    await webPushService.SendPushNotificationAsync(
                        subscription,
                        pushTitle,
                        pushBody,
                        appBaseUri is null ? $"/app/notificari/{notification.Id}" : new Uri(appBaseUri, $"/app/notificari/{notification.Id}").ToString(),
                        cancellationToken);
                    pushSent++;
                }
            }

            // Situația fiscală pleacă o dată pe lună, odată cu anunțul, nu și cu amintirile.
            if (reminder)
            {
                if (notifiedThisUser)
                {
                    usersNotified++;
                }

                continue;
            }

            bool taxThresholdAlreadySent = !request.ForceResend && await context.Notifications.AnyAsync(
                n => n.UserId == userId &&
                     n.Type == NotificationTypes.TaxThreshold &&
                     n.CreatedAtUtc >= monthStartUtc &&
                     n.CreatedAtUtc < monthEndUtc,
                cancellationToken);

            if (!taxThresholdAlreadySent)
            {
                PfaTaxCalculator.TaxThresholdProgress? progress =
                    await BuildTaxThresholdProgressAsync(userId, taxYear, cancellationToken);

                if (progress is not null)
                {
                    var taxNotification = new Notification
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        Text = TaxThresholdTexts.BuildNotificationText(taxYear, progress),
                        Type = NotificationTypes.TaxThreshold,
                        IsRead = false,
                        CreatedAtUtc = nowUtc,
                    };

                    context.Notifications.Add(taxNotification);
                    inAppCreated++;
                    taxThresholdCreated++;
                    notifiedThisUser = true;

                    subscriptions ??= await GetPushSubscriptionsAsync(userId, cancellationToken);
                    string taxPushBody = TaxThresholdTexts.BuildPushNotificationText(taxYear, progress);

                    foreach (PushSubscription subscription in subscriptions)
                    {
                        await webPushService.SendPushNotificationAsync(
                            subscription,
                            TaxThresholdTexts.PushTitle,
                            taxPushBody,
                            appBaseUri is null ? $"/app/notificari/{taxNotification.Id}" : new Uri(appBaseUri, $"/app/notificari/{taxNotification.Id}").ToString(),
                            cancellationToken);
                        pushSent++;
                    }
                }
            }

            if (notifiedThisUser)
            {
                usersNotified++;
            }
        }

        if (inAppCreated > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return new SendRecurringDocumentationNotificationsResult(
            usersNotified,
            inAppCreated,
            pushSent,
            recurringDocumentationCreated,
            taxThresholdCreated);
    }

    private async Task<List<PushSubscription>> GetPushSubscriptionsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await context.PushSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken);

    private async Task<PfaTaxCalculator.TaxThresholdProgress?> BuildTaxThresholdProgressAsync(
        Guid userId,
        int year,
        CancellationToken cancellationToken)
    {
        PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (pfa is null)
        {
            return null;
        }

        List<PfaMonthlyIncome> incomes = await context.PfaMonthlyIncomes
            .AsNoTracking()
            .Where(i => i.PfaRegistrationId == pfa.Id && i.Year == year)
            .ToListAsync(cancellationToken);

        decimal totalIncome = incomes.Sum(i => i.ComputeVenitTotal());

        var expensesWithStatus = await context.DeductibleExpenses
            .AsNoTracking()
            .Where(e => e.PfaRegistrationId == pfa.Id && e.Year == year)
            .Join(
                context.Documents.AsNoTracking(),
                e => e.DocumentId,
                d => d.Id,
                (e, d) => new
                {
                    e.AmountRon,
                    d.Status
                })
            .ToListAsync(cancellationToken);

        decimal verifiedExpenses = expensesWithStatus
            .Where(e => e.Status == DocumentStatus.Verified)
            .Sum(e => e.AmountRon ?? 0m);

        if (totalIncome <= 0m && verifiedExpenses <= 0m)
        {
            return null;
        }

        return PfaTaxCalculator.ComputeThresholdProgress(totalIncome, verifiedExpenses, year, taxParameters.For(year));
    }
}
