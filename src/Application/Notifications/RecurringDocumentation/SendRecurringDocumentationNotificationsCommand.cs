using Application.Abstractions.Messaging;

namespace Application.Notifications.RecurringDocumentation;

/// <summary>Ce pleacă: anunțul de închidere a lunii (pe 1) sau amintirea (pe 4 și 5).</summary>
public enum RecurringDocumentationKind
{
    Notice = 0,
    Reminder = 1,
}

/// <param name="RequireScheduledDay">
/// Trimite doar în ziua programată: 1 pentru anunț, 4 și 5 pentru amintire. Jobul zilnic o cere;
/// aplicația deschisă de client nu — ea trimite anunțul oricând între 1 și 5, dacă n-a plecat încă.
/// </param>
public sealed record SendRecurringDocumentationNotificationsCommand(
    Guid? TargetUserId,
    bool RequireScheduledDay,
    bool ForceResend,
    RecurringDocumentationKind Kind = RecurringDocumentationKind.Notice) : ICommand<SendRecurringDocumentationNotificationsResult>;

public sealed record SendRecurringDocumentationNotificationsResult(
    int UsersNotified,
    int InAppCreated,
    int PushSent,
    int RecurringDocumentationCreated = 0,
    int TaxThresholdCreated = 0);
