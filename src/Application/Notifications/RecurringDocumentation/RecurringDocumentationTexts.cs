using System.Globalization;

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

    public static string BuildNotificationText(DateTime? referenceUtc = null)
    {
        DateTime reference = referenceUtc ?? DateTime.UtcNow;
        string monthLabel = FormatRomaniaMonth(reference);
        string checklist = string.Join(", ", RequiredDocuments);
        return $"Este începutul lunii ({monthLabel}). Te rugăm să încarci documentația recurentă: {checklist}.";
    }

    public static string BuildDeepLink(Uri? appBaseUri)
    {
        const string path = "/app/dashboard?section=doc_recurring";
        return appBaseUri is null ? path : new Uri(appBaseUri, path).ToString();
    }

    public static (int Year, int Month) GetRomaniaYearMonth(DateTime utcNow)
    {
        var romania = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, romania);
        return (local.Year, local.Month);
    }

    public static bool IsFirstDayOfMonthInRomania(DateTime utcNow)
    {
        var romania = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, romania);
        return local.Day == 1;
    }

    public static (DateTime StartUtc, DateTime EndUtc) GetRomaniaMonthBoundsUtc(DateTime referenceUtc)
    {
        var romania = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(referenceUtc, romania);
        var startLocal = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        DateTime endLocal = startLocal.AddMonths(1);
        return (
            TimeZoneInfo.ConvertTimeToUtc(startLocal, romania),
            TimeZoneInfo.ConvertTimeToUtc(endLocal, romania));
    }

    private static string FormatRomaniaMonth(DateTime utcNow)
    {
        var romania = TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, romania);
        return local.ToString("MMMM yyyy", new CultureInfo("ro-RO"));
    }
}
