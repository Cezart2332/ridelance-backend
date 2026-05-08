using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Notifications.GetNotifications;

internal sealed class GetNotificationsQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetNotificationsQuery, IReadOnlyList<NotificationResponse>>
{
    public async Task<Result<IReadOnlyList<NotificationResponse>>> Handle(
        GetNotificationsQuery request,
        CancellationToken cancellationToken)
    {
        List<NotificationResponse> notifications = await context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == request.UserId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => NotificationResponse.FromEntity(n))
            .ToListAsync(cancellationToken);

        return notifications;
    }
}
