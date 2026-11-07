using Application.Accounting;

namespace Application.Notifications.RecurringDocumentation;

/// <summary>
/// Textele închiderii lunii. Banca, rapoartele Uber/Bolt și facturile din SPV vin automat; de la
/// PFA se cer doar bonurile fiscale, până pe <see cref="AccountingPeriod.CloseDay"/>.
/// </summary>
public static class RecurringDocumentationTexts
{
    public const string PushTitle = "Închiderea lunii – documente necesare";
    public const string ReminderPushTitle = "Ultimele zile pentru documentele lunare";

    /// <summary>Anunțul de pe 1: luna trecută s-a încheiat, ce mai are PFA-ul de făcut.</summary>
    public static string BuildNotificationText(DateTime? referenceUtc = null)
    {
        (int _, int month) = AccountingPeriod.RequestedMonthUtc(referenceUtc ?? DateTime.UtcNow);
        return $"Luna {AccountingPeriod.MonthName(month)} s-a încheiat! RIDElance preia automat tranzacțiile bancare, " +
               "rapoartele Uber/Bolt și facturile din SPV.\n\n" +
               $"Tot ce trebuie să faci: încarcă în aplicație, până pe {AccountingPeriod.CloseDay} ale lunii, bonurile fiscale " +
               "pentru cheltuielile deductibile, care trebuie să conțină CUI-ul PFA-ului tău.\n\n" +
               "Dacă nu ai bonuri de încărcat, nu trebuie să faci nimic. De restul ne ocupăm noi!";
    }

    public static string BuildPushNotificationText(DateTime? referenceUtc = null)
    {
        (int _, int month) = AccountingPeriod.RequestedMonthUtc(referenceUtc ?? DateTime.UtcNow);
        return $"Luna {AccountingPeriod.MonthName(month)} s-a încheiat. Încarcă bonurile fiscale cu CUI-ul PFA-ului " +
               $"până pe {AccountingPeriod.CloseDay} ale lunii.";
    }

    /// <summary>Amintirea de pe 4 și 5, pentru cine n-a confirmat luna.</summary>
    public static readonly string ReminderText =
        $"Ai timp până pe {AccountingPeriod.CloseDay} ale lunii să încarci bonurile fiscale cu CUI-ul PFA-ului pentru luna trecută.\n\n" +
        "Restul documentelor sunt preluate automat de RIDElance. Dacă nu ai bonuri de adăugat, confirmă că ai finalizat.";

    public static readonly string ReminderPushText =
        $"Ai timp până pe {AccountingPeriod.CloseDay} ale lunii să încarci bonurile fiscale pentru luna trecută.";

    public static string BuildDeepLink(Uri? appBaseUri)
    {
        const string path = "/app/dashboard/documente/recurente";
        return appBaseUri is null ? path : new Uri(appBaseUri, path).ToString();
    }
}
