namespace Application.Cars;

/// <summary>
/// De unde a venit vizitatorul, redus la un șir scurt și previzibil.
///
/// Clientul trimite ce are în URL — `utm_source`, sau numele paginii interne de pe care a plecat.
/// Normalizarea se face aici, o singură dată, pentru că sursa intră în două tabele diferite
/// (<c>car_views</c> și <c>car_leads</c>) și e o valoare venită din afară: un `utm_source` e un
/// parametru de URL pe care oricine îl poate scrie cum vrea, inclusiv de 4000 de caractere.
/// </summary>
public static class TrafficSource
{
    /// <summary>Cât încape în coloană. Vezi configurațiile celor două entități.</summary>
    public const int MaxLength = 32;

    /// <summary>Vizita a venit direct pe pagina anunțului, fără nimic de reținut.</summary>
    public const string Direct = "vdp";

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Direct;
        }

        string trimmed = raw.Trim();

        // Doar litere, cifre și separatoarele obișnuite din utm-uri. Restul cade: sursa ajunge
        // afișată în dashboard, iar ce vine din URL nu se afișează nefiltrat.
        string cleaned = new([.. trimmed.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')]);

        if (cleaned.Length == 0)
        {
            return Direct;
        }

        return cleaned.Length > MaxLength ? cleaned[..MaxLength] : cleaned;
    }
}
