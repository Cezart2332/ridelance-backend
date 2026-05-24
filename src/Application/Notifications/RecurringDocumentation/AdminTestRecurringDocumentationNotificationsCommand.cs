using Application.Abstractions.Messaging;

namespace Application.Notifications.RecurringDocumentation;

public sealed record AdminTestRecurringDocumentationNotificationsCommand()
    : ICommand<SendRecurringDocumentationNotificationsResult>;
