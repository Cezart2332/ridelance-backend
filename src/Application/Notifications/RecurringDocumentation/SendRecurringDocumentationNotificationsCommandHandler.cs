using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
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
    IConfiguration configuration)
    : ICommandHandler<SendRecurringDocumentationNotificationsCommand, SendRecurringDocumentationNotificationsResult>
{
    public async Task<Result<SendRecurringDocumentationNotificationsResult>> Handle(
        SendRecurringDocumentationNotificationsCommand request,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;

        if (request.RequireFirstOfMonth && !RecurringDocumentationTexts.IsFirstDayOfMonthInRomania(nowUtc))
        {
            return new SendRecurringDocumentationNotificationsResult(0, 0, 0);
        }

        (DateTime monthStartUtc, DateTime monthEndUtc) =
            RecurringDocumentationTexts.GetRomaniaMonthBoundsUtc(nowUtc);
        (int taxYear, _) = RecurringDocumentationTexts.GetRomaniaYearMonth(nowUtc);
        string notificationText = RecurringDocumentationTexts.BuildNotificationText(nowUtc);
        Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase)
            ? parsedBase
            : null;
        string deepLink = RecurringDocumentationTexts.BuildDeepLink(appBaseUri);

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

        int inAppCreated = 0;
        int pushSent = 0;
        int usersNotified = 0;
        int recurringDocumentationCreated = 0;
        int taxThresholdCreated = 0;

        foreach (Guid userId in userIds)
        {
            bool notifiedThisUser = false;
            List<PushSubscription>? subscriptions = null;

            bool documentationAlreadySent = !request.ForceResend && await context.Notifications.AnyAsync(
                n => n.UserId == userId &&
                     n.Type == NotificationTypes.RecurringDocumentation &&
                     n.CreatedAtUtc >= monthStartUtc &&
                     n.CreatedAtUtc < monthEndUtc,
                cancellationToken);

            if (!documentationAlreadySent)
            {
                var notification = new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Text = notificationText,
                    Type = NotificationTypes.RecurringDocumentation,
                    IsRead = false,
                    CreatedAtUtc = nowUtc,
                };

                context.Notifications.Add(notification);
                inAppCreated++;
                recurringDocumentationCreated++;
                notifiedThisUser = true;

                subscriptions = await GetPushSubscriptionsAsync(userId, cancellationToken);

                string pushBody = RecurringDocumentationTexts.BuildPushNotificationText(nowUtc);

                foreach (PushSubscription subscription in subscriptions)
                {
                    await webPushService.SendPushNotificationAsync(
                        subscription,
                        RecurringDocumentationTexts.PushTitle,
                        pushBody,
                        deepLink,
                        cancellationToken);
                    pushSent++;
                }
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
                            deepLink,
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

        return PfaTaxCalculator.ComputeThresholdProgress(totalIncome, verifiedExpenses, year);
    }
}
