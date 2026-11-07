using System.Globalization;

namespace Application.Accounting;

/// <summary>
/// Regula lunii contabile a platformei, scrisă o singură dată.
/// </summary>
/// <remarks>
/// <para>
/// Bonurile pentru o lună se primesc până pe <see cref="CloseDay"/> ale lunii următoare (restul
/// documentelor vin automat: bancă, Uber/Bolt, SPV). Un document încărcat de pe <c>CloseDay + 1</c>
/// ale unei luni până pe <c>CloseDay</c> ale lunii următoare se socotește la luna aceea: pentru
/// octombrie, de pe 6 octombrie până pe 5 noiembrie inclusiv. Pe 1 ale lunii pleacă anunțul de
/// închidere a lunii trecute, iar pe 4 și 5 amintirea, doar pentru cine n-a confirmat.
/// </para>
/// <para>
/// De aici decurg trei lucruri: ce lună se cere într-o zi dată (<see cref="RequestedMonth"/>), ce
/// documente intră în luna aceea după data încărcării (<see cref="CollectionWindowUtc"/>) și până
/// când (<see cref="Deadline"/>). Toate orele sunt ale României.
/// </para>
/// </remarks>
public static class AccountingPeriod
{
    /// <summary>Ultima zi din luna următoare în care se mai primesc bonurile lunii.</summary>
    public const int CloseDay = 5;

    /// <summary>Ziua în care pleacă anunțul „Închiderea lunii”.</summary>
    public const int NoticeDay = 1;

    /// <summary>Zilele cu amintirea „Ultimele zile”, pentru cine n-a confirmat luna.</summary>
    public static readonly IReadOnlySet<int> ReminderDays = new HashSet<int> { 4, 5 };

    private static readonly CultureInfo Romanian = new("ro-RO");

    /// <summary>
    /// Luna pentru care se cer documente în ziua dată: de pe 6 încolo, luna curentă; până pe 5
    /// inclusiv, luna trecută (a cărei fereastră încă e deschisă).
    /// </summary>
    public static (int Year, int Month) RequestedMonth(DateOnly localDay)
    {
        var month = new DateOnly(localDay.Year, localDay.Month, 1);
        DateOnly requested = localDay.Day > CloseDay ? month : month.AddMonths(-1);
        return (requested.Year, requested.Month);
    }

    public static (int Year, int Month) RequestedMonthUtc(DateTime utcNow) =>
        RequestedMonth(DateOnly.FromDateTime(ToRomania(utcNow)));

    /// <summary>Zilele în care luna trecută încă se poate completa: de pe 1 până pe <see cref="CloseDay"/>.</summary>
    public static bool IsClosingWindow(DateOnly localDay) => localDay.Day <= CloseDay;

    /// <summary>Ultima zi în care se mai primesc documente pentru luna dată.</summary>
    public static DateOnly Deadline(int year, int month) =>
        new DateOnly(year, month, 1).AddMonths(1).AddDays(CloseDay - 1);

    /// <summary>
    /// Intervalul [început, sfârșit) în care un document încărcat se socotește la luna dată:
    /// de pe 6 ale lunii, ora 00:00, până pe 6 ale lunii următoare, ora 00:00.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) CollectionWindowUtc(int year, int month)
    {
        TimeZoneInfo romania = RomaniaTimeZone();
        var startLocal = new DateTime(year, month, CloseDay + 1, 0, 0, 0, DateTimeKind.Unspecified);
        DateTime endLocal = startLocal.AddMonths(1);
        return (
            TimeZoneInfo.ConvertTimeToUtc(startLocal, romania),
            TimeZoneInfo.ConvertTimeToUtc(endLocal, romania));
    }

    /// <summary>„septembrie 2026".</summary>
    public static string MonthLabel(int year, int month) =>
        new DateOnly(year, month, 1).ToString("MMMM yyyy", Romanian);

    /// <summary>„octombrie".</summary>
    public static string MonthName(int month) =>
        new DateOnly(2000, month, 1).ToString("MMMM", Romanian);

    /// <summary>„5 noiembrie".</summary>
    public static string DeadlineLabel(int year, int month) =>
        Deadline(year, month).ToString("d MMMM", Romanian);

    /// <summary>Cheia lunii, „2026-10”: pe notificări și pe confirmarea lunii.</summary>
    public static string MonthKey(int year, int month) =>
        string.Create(CultureInfo.InvariantCulture, $"{year:D4}-{month:D2}");

    public static DateTime ToRomania(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), RomaniaTimeZone());

    private static TimeZoneInfo RomaniaTimeZone()
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
