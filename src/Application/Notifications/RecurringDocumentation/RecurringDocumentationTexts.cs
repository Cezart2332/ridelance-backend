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
        string monthLabel = FormatPreviousRomaniaMonth(reference);
        string checklist = string.Join(", ", RequiredDocuments);
        return $"Este începutul lunii. Te rugăm să încarci documentația recurentă pentru {monthLabel}: {checklist}.";
    }

    public static string BuildPushNotificationText(DateTime? referenceUtc = null)
    {
        DateTime reference = referenceUtc ?? DateTime.UtcNow;
        string monthLabel = FormatPreviousRomaniaMonth(reference);
        return $"Te rugăm să încarci documentele pentru {monthLabel}.";
    }

    public static string BuildDeepLink(Uri? appBaseUri)
    {
        const string path = "/app/dashboard?section=doc_recurring";
        return appBaseUri is null ? path : new Uri(appBaseUri, path).ToString();
    }

    public static (int Year, int Month) GetRomaniaYearMonth(DateTime utcNow)
    {
        TimeZoneInfo romania = GetRomaniaTimeZone();
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, romania);
        return (local.Year, local.Month);
    }

    public static bool IsFirstDayOfMonthInRomania(DateTime utcNow)
    {
        TimeZoneInfo romania = GetRomaniaTimeZone();
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, romania);
        return local.Day == 1;
    }

    public static (DateTime StartUtc, DateTime EndUtc) GetRomaniaMonthBoundsUtc(DateTime referenceUtc)
    {
        TimeZoneInfo romania = GetRomaniaTimeZone();
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(referenceUtc, romania);
        var startLocal = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        DateTime endLocal = startLocal.AddMonths(1);
        return (
            TimeZoneInfo.ConvertTimeToUtc(startLocal, romania),
            TimeZoneInfo.ConvertTimeToUtc(endLocal, romania));
    }

    private static string FormatPreviousRomaniaMonth(DateTime utcNow)
    {
        TimeZoneInfo romania = GetRomaniaTimeZone();
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, romania);
        DateTime previousMonth = local.AddMonths(-1);
        return previousMonth.ToString("MMMM yyyy", new CultureInfo("ro-RO"));
    }

    private static TimeZoneInfo GetRomaniaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        }
    }
}
