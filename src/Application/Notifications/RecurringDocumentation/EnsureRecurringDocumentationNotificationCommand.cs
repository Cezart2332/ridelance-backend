using Application.Abstractions.Messaging;

namespace Application.Notifications.RecurringDocumentation;

public sealed record EnsureRecurringDocumentationNotificationCommand(Guid UserId)
    : ICommand<EnsureRecurringDocumentationNotificationResult>;

public sealed record EnsureRecurringDocumentationNotificationResult(
    bool Created,
    Guid? NotificationId,
    bool PushSent);
