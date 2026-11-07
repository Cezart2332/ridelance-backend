namespace Application.Bolt;

/// <summary>
/// De când cerem cursele la Bolt. Fleet API 2.0 filtrează după data cursei, nu după revizia de
/// preț: 7 zile prind și corecturile întârziate. Dacă ultima sincronizare e mai veche (integrare
/// nouă sau căzută o vreme), recuperăm golul, dar nu mai devreme de 1 ianuarie al anului.
/// </summary>
public static class BoltSyncWindow
{
    public static readonly TimeSpan Overlap = TimeSpan.FromDays(7);

    public static DateTime Start(DateTime? lastFetchedAtUtc, DateTime nowUtc)
    {
        var yearStart = new DateTime(nowUtc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime regular = nowUtc - Overlap;
        // Golul se recuperează doar din anul curent; suprapunerea de 7 zile rămâne întreagă și în ianuarie.
        DateTime gap = lastFetchedAtUtc is DateTime last && last - Overlap > yearStart ? last - Overlap : yearStart;
        return gap < regular ? gap : regular;
    }
}
