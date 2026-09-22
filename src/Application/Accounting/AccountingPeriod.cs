using System.Globalization;

namespace Application.Accounting;

/// <summary>
/// Regula lunii contabile a platformei, scrisă o singură dată.
/// </summary>
/// <remarks>
/// <para>
/// Contabilul închide luna pe <see cref="CloseDay"/> ale lunii următoare. Documentele pentru o lună
/// se cer de pe <c>CloseDay + 1</c> ale ei până pe <c>CloseDay</c> ale lunii următoare: pentru
/// septembrie, de pe 26 septembrie până pe 25 octombrie inclusiv.
/// </para>
/// <para>
/// De aici decurg trei lucruri: ce lună se cere într-o zi dată (<see cref="RequestedMonth"/>), ce
/// documente intră în luna aceea după data încărcării (<see cref="CollectionWindowUtc"/>) și până
/// când (<see cref="Deadline"/>). Toate orele sunt ale României.
/// </para>
/// </remarks>
public static class AccountingPeriod
{
    /// <summary>Ziua din luna următoare în care contabilul închide luna.</summary>
    public const int CloseDay = 25;

    private static readonly CultureInfo Romanian = new("ro-RO");

    /// <summary>
    /// Luna pentru care se cer documente în ziua dată: de pe 26 încolo, luna curentă; până pe 25
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

    /// <summary>Ziua în care se deschide fereastra unei luni — și în care se trimite cererea.</summary>
    public static bool IsCollectionStartDay(DateOnly localDay) => localDay.Day == CloseDay + 1;

    /// <summary>Ultima zi în care se mai primesc documente pentru luna dată.</summary>
    public static DateOnly Deadline(int year, int month) =>
        new DateOnly(year, month, 1).AddMonths(1).AddDays(CloseDay - 1);

    /// <summary>
    /// Intervalul [început, sfârșit) în care un document încărcat se socotește la luna dată:
    /// de pe 26 ale lunii, ora 00:00, până pe 26 ale lunii următoare, ora 00:00.
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

    /// <summary>„25 octombrie".</summary>
    public static string DeadlineLabel(int year, int month) =>
        Deadline(year, month).ToString("d MMMM", Romanian);

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
