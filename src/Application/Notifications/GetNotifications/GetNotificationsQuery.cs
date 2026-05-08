using Application.Abstractions.Messaging;

namespace Application.Notifications.GetNotifications;

public sealed record GetNotificationsQuery(Guid UserId) : IQuery<IReadOnlyList<NotificationResponse>>;
