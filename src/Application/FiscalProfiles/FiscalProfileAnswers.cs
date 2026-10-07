namespace Application.FiscalProfiles;

/// <summary>
/// Situația fiscală a PFA-ului: singurele lucruri care schimbă CAS și CASS pentru un șofer
/// ridesharing. Fiecare e <c>yes</c> / <c>no</c>; „Niciuna” înseamnă toate trei <c>no</c>.
/// Se pot combina — un pensionar angajat are ambele excepții.
/// </summary>
/// <remarks>
/// Înainte era un formular de 22 de întrebări (alte venituri, pierderi, opțiuni CASS, CAS
/// voluntar, documente din perioade anterioare). Au fost scoase: un șofer le răspundea greu,
/// iar calculul îl face oricum contabilul. Răspunsurile vechi din <c>AnswersJson</c> se ignoră la
/// citire; doar <c>employment: "full"</c> se traduce în <see cref="EmployedFullTime"/>.
/// </remarks>
public sealed record FiscalProfileAnswers
{
    /// <summary>Are calitatea de pensionar (Codul fiscal art. 150 alin. (1), art. 174 alin. (7) lit. c)).</summary>
    public string? Pensioner { get; init; }

    /// <summary>Elev sau student, sub 26 de ani (art. 154 alin. (1) lit. a), art. 174 alin. (8) lit. a)).</summary>
    public string? Student { get; init; }

    /// <summary>Angajat cu normă întreagă în altă parte (art. 174 alin. (7) lit. a)).</summary>
    public string? EmployedFullTime { get; init; }
}

/// <summary>Un câmp schimbat, pentru istoricul reviziilor.</summary>
public sealed record FiscalProfileFieldChange(string Field, string? OldValue, string? NewValue);
