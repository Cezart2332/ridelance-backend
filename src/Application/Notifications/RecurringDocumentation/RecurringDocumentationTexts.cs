using Application.Accounting;

namespace Application.Notifications.RecurringDocumentation;

public static class RecurringDocumentationTexts
{
    private static readonly string[] RequiredDocuments =
    [
        "Extrase bancare (toate conturile)",
        "Raport venituri Uber",
        "Raport venituri Bolt",
        "Facturi cheltuieli deductibile",
    ];

    public const string PushTitle = "Documentație recurentă";

    /// <summary>
    /// Cererea pentru luna contabilă curentă (vezi <see cref="AccountingPeriod"/>): ce lună și până
    /// când. Pe 26 septembrie: „documentele pentru septembrie 2026, până pe 25 octombrie".
    /// </summary>
    public static string BuildNotificationText(DateTime? referenceUtc = null)
    {
        (int year, int month) = AccountingPeriod.RequestedMonthUtc(referenceUtc ?? DateTime.UtcNow);
        string checklist = string.Join(", ", RequiredDocuments);
        return $"Te rugăm să încarci documentele pentru {AccountingPeriod.MonthLabel(year, month)}, " +
               $"până pe {AccountingPeriod.DeadlineLabel(year, month)}: {checklist}.";
    }

    public static string BuildPushNotificationText(DateTime? referenceUtc = null)
    {
        (int year, int month) = AccountingPeriod.RequestedMonthUtc(referenceUtc ?? DateTime.UtcNow);
        return $"Încarcă documentele pentru {AccountingPeriod.MonthLabel(year, month)} până pe " +
               $"{AccountingPeriod.DeadlineLabel(year, month)}.";
    }

    public static string BuildDeepLink(Uri? appBaseUri)
    {
        const string path = "/app/dashboard/documente/recurente";
        return appBaseUri is null ? path : new Uri(appBaseUri, path).ToString();
    }
}
