using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Domain.Notifications;
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

        foreach (Guid userId in userIds)
        {
            if (!request.ForceResend)
            {
                bool alreadySent = await context.Notifications.AnyAsync(
                    n => n.UserId == userId &&
                         n.Type == NotificationTypes.RecurringDocumentation &&
                         n.CreatedAtUtc >= monthStartUtc &&
                         n.CreatedAtUtc < monthEndUtc,
                    cancellationToken);

                if (alreadySent)
                {
                    continue;
                }
            }

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
            usersNotified++;

            List<PushSubscription> subscriptions = await context.PushSubscriptions
                .Where(s => s.UserId == userId)
                .ToListAsync(cancellationToken);

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

        if (inAppCreated > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return new SendRecurringDocumentationNotificationsResult(usersNotified, inAppCreated, pushSent);
    }
}
