using Domain.Users;
using SharedKernel;

namespace Domain.Notifications;

/// <summary>
/// Categoriile pe care utilizatorul le poate porni sau opri.
///
/// Separarea operațional/comercial e cerută explicit de spec §10.5 și e cea care contează:
/// cineva care nu vrea oferte trebuie să poată tăia ofertele fără să piardă anunțul că îi
/// expiră RCA-ul. De aceea sunt categorii distincte, nu un singur comutator.
/// </summary>
public enum NotificationCategory
{
    // ── Operațional ──
    DocumentExpiry = 0,
    TaxesAndDeadlines = 1,
    AccountantMessages = 2,
    PlatformSyncIssues = 3,

    // ── Comercial ──
    RidelanceUpdates = 100,
    OffersAndBenefits = 101,
}

/// <summary>
/// Preferința unui utilizator pentru o categorie. Absența unui rând înseamnă „activ": cine nu
/// s-a atins de setări primește tot ce e util, iar tabela crește doar cu deciziile explicite.
/// </summary>
public sealed class NotificationPreference : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationCategory Category { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;

    /// <summary>Comercialul e opt-out la fel ca operaționalul, dar se grupează separat în UI.</summary>
    public static bool IsCommercial(NotificationCategory category) =>
        category >= NotificationCategory.RidelanceUpdates;

    /// <summary>
    /// Ce categorie acoperă un tip de notificare. Tipurile neacoperite nu sunt filtrabile —
    /// sunt anunțuri de sistem (status PFA, pași de onboarding), pe care nu le poți opri.
    /// </summary>
    public static NotificationCategory? CategoryForType(string notificationType) => notificationType switch
    {
        NotificationTypes.DocumentExpiringSoon => NotificationCategory.DocumentExpiry,
        NotificationTypes.RecurringDocumentation => NotificationCategory.DocumentExpiry,
        NotificationTypes.TaxThreshold => NotificationCategory.TaxesAndDeadlines,
        NotificationTypes.ChatRoomMessage => NotificationCategory.AccountantMessages,
        NotificationTypes.BankConnection => NotificationCategory.PlatformSyncIssues,
        NotificationTypes.FleetAccountConfigured => NotificationCategory.PlatformSyncIssues,
        _ => null,
    };
}
