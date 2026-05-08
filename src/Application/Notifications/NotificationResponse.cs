using Domain.Notifications;

namespace Application.Notifications;

public sealed record NotificationResponse(
    Guid Id,
    string Text,
    string Type,
    bool IsRead,
    DateTime CreatedAtUtc)
{
    public static NotificationResponse FromEntity(Notification notification) =>
        new(
            notification.Id,
            notification.Text,
            notification.Type,
            notification.IsRead,
            notification.CreatedAtUtc);
}
