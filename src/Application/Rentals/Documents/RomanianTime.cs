using System.Globalization;

namespace Application.Rentals.Documents;

/// <summary>
/// Datele de pe documente, în ora României, oricare ar fi fusul serverului.
/// </summary>
/// <remarks>
/// Containerul rulează în UTC, deci <see cref="DateTime.ToLocalTime"/> nu schimba nimic: un
/// contract generat la 00:30, ora României, ieșea datat cu ziua de ieri („încheiat astăzi, …”).
/// </remarks>
public static class RomanianTime
{
    private static readonly TimeZoneInfo Zone = FindZone();

    public static DateTime ToLocal(DateTime value)
    {
        DateTime utc = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value,
        };

        return TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
    }

    /// <summary>„dd.MM.yyyy”, ca pe șablon.</summary>
    public static string Date(DateTime value) =>
        ToLocal(value).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static TimeZoneInfo FindZone()
    {
        // IANA pe Linux, Windows pe Windows; .NET le traduce între ele cu ICU, dar nu peste tot.
        foreach (string id in new[] { "Europe/Bucharest", "E. Europe Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // Următorul nume din listă; dacă niciunul nu există, rămâne UTC.
                continue;
            }
        }

        return TimeZoneInfo.Utc;
    }
}
