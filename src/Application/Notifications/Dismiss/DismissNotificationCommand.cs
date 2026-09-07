using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Notifications.Dismiss;

public sealed record DismissNotificationCommand(Guid UserId, Guid NotificationId) : ICommand;

internal sealed class DismissNotificationCommandHandler(IApplicationDbContext context)
    : ICommandHandler<DismissNotificationCommand>
{
    public async Task<Result> Handle(DismissNotificationCommand request, CancellationToken cancellationToken)
    {
        Domain.Notifications.Notification? notification = await context.Notifications
            .SingleOrDefaultAsync(n => n.Id == request.NotificationId && n.UserId == request.UserId, cancellationToken);
        if (notification is not null)
        {
            // Preserve deduplication history so scheduled reminders do not recreate deleted items.
            notification.IsDismissed = true;
            notification.IsRead = true;
            await context.SaveChangesAsync(cancellationToken);
        }
        return Result.Success();
    }
}
