using Application.Abstractions.Messaging;

namespace Application.Notifications.MarkAsRead;

public sealed record MarkAsReadCommand(Guid UserId, Guid? NotificationId = null) : ICommand;
