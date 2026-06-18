using Application.Abstractions.Messaging;

namespace Application.Notifications.RecurringDocumentation;

public sealed record SendRecurringDocumentationNotificationsCommand(
    Guid? TargetUserId,
    bool RequireFirstOfMonth,
    bool ForceResend) : ICommand<SendRecurringDocumentationNotificationsResult>;

public sealed record SendRecurringDocumentationNotificationsResult(
    int UsersNotified,
    int InAppCreated,
    int PushSent,
    int RecurringDocumentationCreated = 0,
    int TaxThresholdCreated = 0);
