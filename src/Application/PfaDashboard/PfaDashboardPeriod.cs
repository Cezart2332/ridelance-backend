namespace Application.PfaDashboard;

/// <summary>
/// Un interval calendaristic exprimat în ora României, plus uneltele de care au nevoie
/// ambele query-uri de dashboard: conversia în limite UTC și felierea pe luni.
/// </summary>
internal readonly record struct PfaDashboardPeriod(DateOnly From, DateOnly To)
{
    /// <summary>O felie de lună calendaristică acoperită de interval, cu ponderea ei.</summary>
    internal readonly record struct MonthSlice(int Year, int Month, decimal Weight);

    public int DayCount => To.DayNumber - From.DayNumber + 1;

    /// <summary>Perioada anterioară echivalentă: aceeași lungime, lipită de începutul acesteia.</summary>
    public PfaDashboardPeriod Previous()
    {
        DateOnly previousTo = From.AddDays(-1);
        return new PfaDashboardPeriod(previousTo.AddDays(-(DayCount - 1)), previousTo);
    }

    public (DateTime StartUtc, DateTime EndUtc) ToUtcBounds(TimeZoneInfo timeZone)
    {
        var startLocal = From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var endLocal = To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        return (
            TimeZoneInfo.ConvertTimeToUtc(startLocal, timeZone),
            TimeZoneInfo.ConvertTimeToUtc(endLocal, timeZone));
    }

    /// <summary>
    /// Uber raportează doar totaluri lunare, iar cheltuielile deductibile sunt legate de
    /// (an, lună). Ca să le putem încadra într-un interval arbitrar, fiecare lună atinsă
    /// primește o pondere egală cu fracțiunea de zile acoperite.
    /// </summary>
    public List<MonthSlice> SliceMonths()
    {
        List<MonthSlice> slices = [];
        var cursor = new DateOnly(From.Year, From.Month, 1);

        while (cursor <= To)
        {
            int daysInMonth = DateTime.DaysInMonth(cursor.Year, cursor.Month);
            DateOnly monthEnd = new(cursor.Year, cursor.Month, daysInMonth);
            DateOnly overlapStart = cursor > From ? cursor : From;
            DateOnly overlapEnd = monthEnd < To ? monthEnd : To;
            int overlapDays = overlapEnd.DayNumber - overlapStart.DayNumber + 1;

            if (overlapDays > 0)
            {
                slices.Add(new MonthSlice(cursor.Year, cursor.Month, overlapDays / (decimal)daysInMonth));
            }

            cursor = cursor.AddMonths(1);
        }

        return slices;
    }

    /// <summary>Luna calendaristică de raportare: cea în care cade sfârșitul intervalului.</summary>
    public PfaDashboardPeriod FiscalMonth()
    {
        var start = new DateOnly(To.Year, To.Month, 1);
        return new PfaDashboardPeriod(start, new DateOnly(To.Year, To.Month, DateTime.DaysInMonth(To.Year, To.Month)));
    }

    public bool IsWholeCalendarMonth() =>
        From.Day == 1
        && From.Year == To.Year
        && From.Month == To.Month
        && To.Day == DateTime.DaysInMonth(To.Year, To.Month);

    public static TimeZoneInfo RomaniaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time");
        }
    }

    public static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    public static DateTime ToRomaniaTime(DateTime utc, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeFromUtc(NormalizeUtc(utc), timeZone);
}
