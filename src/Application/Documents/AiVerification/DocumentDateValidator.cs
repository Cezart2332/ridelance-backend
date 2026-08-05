using System.Globalization;

namespace Application.Documents.AiVerification;

/// <summary>Ce se întâmplă cu documentul după verificarea datelor din el.</summary>
public enum DocumentDateOutcome
{
    /// <summary>Datele sunt plauzibile — documentul trece mai departe.</summary>
    Accepted = 0,

    /// <summary>Datele contrazic realitatea (act eliberat în viitor, act expirat). Se respinge.</summary>
    Rejected = 1,

    /// <summary>Datele lipsesc sau sunt implauzibile. Nu respingem — decide un om.</summary>
    NeedsManualReview = 2,
}

/// <param name="Reason">Explicație scurtă, în română, arătată clientului. Gol la <c>Accepted</c>.</param>
public sealed record DocumentDateVerdict(DocumentDateOutcome Outcome, string Reason)
{
    public static readonly DocumentDateVerdict Accepted = new(DocumentDateOutcome.Accepted, string.Empty);

    public bool IsRejected => Outcome == DocumentDateOutcome.Rejected;
    public bool NeedsManualReview => Outcome == DocumentDateOutcome.NeedsManualReview;
}

/// <summary>
/// Verificarea temporală a documentelor, în C#, pe ceasul serverului.
///
/// Modelul de limbaj nu are ceas și nu are voie să judece asta: îi injectam data curentă în
/// prompt și respingea acte valabile pentru că „data eliberării e în viitor". Modelul doar
/// citește datele; deciziile se iau aici, unde sunt deterministe și testabile.
///
/// Regula care lipsea: la certificatul de înregistrare data de pe act este data <b>eliberării</b>,
/// nu a expirării. Un astfel de document nu expiră niciodată — singurul lucru imposibil e să fi
/// fost eliberat în viitor.
/// </summary>
public static class DocumentDateValidator
{
    /// <summary>Sub acest an, o dată citită de OCR e zgomot, nu un act real.</summary>
    private static readonly DateOnly SanityFloor = new(1990, 1, 1);

    /// <summary>Peste atâția ani în viitor, o dată de expirare e o citire greșită, nu un act.</summary>
    private const int MaxYearsAhead = 50;

    /// <summary>
    /// Formatele acceptate la parsare, în ordine. ISO-ul e cel cerut modelului; restul acoperă
    /// cazurile în care întoarce totuși formatul de pe document.
    /// </summary>
    private static readonly string[] AcceptedFormats =
    [
        "yyyy-MM-dd",
        "dd.MM.yyyy",
        "dd/MM/yyyy",
        "dd-MM-yyyy",
        "yyyy/MM/dd",
    ];

    /// <summary>
    /// Ziua curentă în România. Serverul rulează pe UTC, iar între 21:00 și 00:00 vara UTC e
    /// deja „ieri" față de client — destul cât să respingem un act eliberat chiar azi.
    /// </summary>
    public static DateOnly TodayInRomania()
    {
        TimeZoneInfo romania = ResolveRomaniaTimeZone();
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, romania));
    }

    /// <summary>
    /// Parsează o dată citită de OCR. Întoarce null pentru orice nu e o dată reală — inclusiv
    /// pentru citirile stricate de tipul „2O25-09-15" (litera O în loc de zero).
    /// </summary>
    public static DateOnly? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        return DateOnly.TryParseExact(
            trimmed, AcceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Verdictul temporal pentru un document.
    /// </summary>
    /// <param name="issuedOn">Data eliberării, dacă documentul o conține.</param>
    /// <param name="expiresAt">Data expirării, dacă documentul o conține.</param>
    /// <param name="expectsExpiryDate">
    /// Documentul ar trebui să aibă termen de valabilitate. Când e fals, absența ei e normală.
    /// </param>
    /// <param name="issueDateOnly">
    /// Documentul nu expiră: data de pe el e a eliberării. Certificatul de înregistrare, cel
    /// constatator și rezoluția ONRC intră aici — nu au ce expira.
    /// </param>
    /// <param name="validMonthsFromIssue">
    /// Documentul e valabil un număr fix de luni de la eliberare (cazierul: 6). Când documentul
    /// nu tipărește expirarea, o derivăm noi — nu modelul.
    /// </param>
    /// <param name="today">Ziua de referință; în producție <see cref="TodayInRomania"/>.</param>
    public static DocumentDateVerdict Evaluate(
        DateOnly? issuedOn,
        DateOnly? expiresAt,
        bool expectsExpiryDate,
        bool issueDateOnly,
        DateOnly today,
        int? validMonthsFromIssue = null)
    {
        // 1. Data eliberării nu poate fi în viitor, indiferent de tipul documentului.
        //    Egalitatea cu ziua curentă e perfect normală: actul poate fi eliberat chiar azi.
        if (issuedOn is DateOnly issued)
        {
            if (issued > today)
            {
                return new DocumentDateVerdict(
                    DocumentDateOutcome.Rejected,
                    "Data eliberării este în viitor. Verifică dacă ai încărcat documentul corect.");
            }

            if (issued < SanityFloor)
            {
                return new DocumentDateVerdict(
                    DocumentDateOutcome.NeedsManualReview,
                    "Data eliberării nu a putut fi citită corect.");
            }
        }

        // 2. Documentele fără termen (certificate ONRC) se opresc aici: o dată de pe ele nu
        //    înseamnă expirare, oricât de veche ar fi.
        if (issueDateOnly)
        {
            return DocumentDateVerdict.Accepted;
        }

        // Documentele cu termen fix (cazierul: 6 luni) nu tipăresc mereu expirarea — o derivăm
        // din eliberare, în C#, ca să nu punem modelul să facă aritmetică pe date.
        DateOnly? effectiveExpiry = expiresAt;
        if (effectiveExpiry is null && validMonthsFromIssue is int months && issuedOn is DateOnly issuedAt)
        {
            effectiveExpiry = issuedAt.AddMonths(months);
        }

        if (effectiveExpiry is DateOnly expires)
        {
            if (expires < SanityFloor || expires > today.AddYears(MaxYearsAhead))
            {
                return new DocumentDateVerdict(
                    DocumentDateOutcome.NeedsManualReview,
                    "Data de expirare nu a putut fi citită corect.");
            }

            // Un act care expiră azi e încă valabil azi.
            if (expires < today)
            {
                return new DocumentDateVerdict(
                    DocumentDateOutcome.Rejected,
                    "Documentul este expirat. Încarcă unul valabil.");
            }

            // Nu mai verificăm „expirare înainte de eliberare": aici eliberarea e deja ≤ azi și
            // expirarea ≥ azi, deci ordinea lor e garantată. Un act cu datele inversate a fost
            // deja respins mai sus, ca expirat.
            return DocumentDateVerdict.Accepted;
        }

        // 3. Lipsește o dată pe care documentul ar fi trebuit s-o aibă: nu respingem pe baza a
        //    ceva ce nu s-a putut citi — o verifică un om.
        return expectsExpiryDate
            ? new DocumentDateVerdict(
                DocumentDateOutcome.NeedsManualReview,
                "Nu am putut citi data de valabilitate.")
            : DocumentDateVerdict.Accepted;
    }

    /// <summary>Windows și Linux numesc altfel același fus; îl căutăm sub ambele denumiri.</summary>
    private static TimeZoneInfo ResolveRomaniaTimeZone()
    {
        foreach (string id in (string[])["Europe/Bucharest", "GTB Standard Time"])
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Încercăm următoarea denumire.
            }
            catch (InvalidTimeZoneException)
            {
                // Bază de fusuri stricată — cădem pe UTC mai jos.
            }
        }

        return TimeZoneInfo.Utc;
    }
}
