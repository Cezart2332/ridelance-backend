using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Notifications.MarkAsRead;

internal sealed class MarkAsReadCommandHandler(IApplicationDbContext context)
    : ICommandHandler<MarkAsReadCommand>
{
    public async Task<Result> Handle(
        MarkAsReadCommand request,
        CancellationToken cancellationToken)
    {
        IQueryable<Domain.Notifications.Notification> query = context.Notifications.Where(n => n.UserId == request.UserId);

        if (request.NotificationId.HasValue)
        {
            query = query.Where(n => n.Id == request.NotificationId.Value);
        }

        List<Domain.Notifications.Notification> notifications = await query.ToListAsync(cancellationToken);

        if (notifications.Count == 0)
        {
            return Result.Success();
        }

        foreach (Domain.Notifications.Notification notification in notifications)
        {
            notification.IsRead = true;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
