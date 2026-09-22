using Application.Abstractions.Messaging;

namespace Application.Notifications.RecurringDocumentation;

/// <param name="RequireCollectionStartDay">
/// Trimite doar în ziua în care se deschide fereastra lunii (26). Jobul zilnic o cere; aplicația
/// deschisă de client nu — ea trimite oricând în fereastră, dacă cererea n-a plecat încă.
/// </param>
public sealed record SendRecurringDocumentationNotificationsCommand(
    Guid? TargetUserId,
    bool RequireCollectionStartDay,
    bool ForceResend) : ICommand<SendRecurringDocumentationNotificationsResult>;

public sealed record SendRecurringDocumentationNotificationsResult(
    int UsersNotified,
    int InAppCreated,
    int PushSent,
    int RecurringDocumentationCreated = 0,
    int TaxThresholdCreated = 0);
