using Application.Abstractions.Messaging;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions.Data;
using Domain.Notifications;
using SharedKernel;

namespace Application.Notifications.RecurringDocumentation;

internal sealed class EnsureRecurringDocumentationNotificationCommandHandler(
    IApplicationDbContext context,
    ICommandHandler<SendRecurringDocumentationNotificationsCommand, SendRecurringDocumentationNotificationsResult> sendHandler)
    : ICommandHandler<EnsureRecurringDocumentationNotificationCommand, EnsureRecurringDocumentationNotificationResult>
{
    public async Task<Result<EnsureRecurringDocumentationNotificationResult>> Handle(
        EnsureRecurringDocumentationNotificationCommand request,
        CancellationToken cancellationToken)
    {
        Result<SendRecurringDocumentationNotificationsResult> sendResult = await sendHandler.Handle(
            new SendRecurringDocumentationNotificationsCommand(
                request.UserId,
                RequireFirstOfMonth: true,
                ForceResend: false),
            cancellationToken);

        if (sendResult.IsFailure)
        {
            return Result.Failure<EnsureRecurringDocumentationNotificationResult>(sendResult.Error);
        }

        if (sendResult.Value.RecurringDocumentationCreated == 0)
        {
            return new EnsureRecurringDocumentationNotificationResult(false, null, false);
        }

        Notification? latest = await context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == request.UserId && n.Type == NotificationTypes.RecurringDocumentation)
            .OrderByDescending(n => n.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return new EnsureRecurringDocumentationNotificationResult(
            true,
            latest?.Id,
            sendResult.Value.PushSent > 0);
    }
}
