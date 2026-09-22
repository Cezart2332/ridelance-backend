namespace Application.FiscalProfiles;

/// <summary>
/// Răspunsurile din formularul profilului fiscal, cu exact cheile din spec (§4). Tipat, nu un
/// dicționar liber: o cheie scrisă greșit în frontend nu ajunge în baza de date.
/// </summary>
/// <remarks>
/// Toate sunt opționale aici, pentru că ciorna se salvează la fiecare pas. Ce e obligatoriu și
/// când decide <see cref="FiscalProfileSchema"/>. Nicio întrebare nu are varianta „Nu știu”.
/// </remarks>
public sealed record FiscalProfileAnswers
{
    // Pasul 1 — Datele PFA
    public string? DataCorrect { get; init; }
    public string? CorrectionDetails { get; init; }
    public string? PriorDocs { get; init; }
    public string? PriorDocsLocation { get; init; }

    // Pasul 2 — Situația ta
    public string? Employment { get; init; }
    public DateOnly? EmploymentStart { get; init; }
    public DateOnly? EmploymentEnd { get; init; }
    public string? Pensioner { get; init; }
    public DateOnly? PensionerSince { get; init; }
    public string? Student { get; init; }
    public string? OwnPensionSystem { get; init; }
    public string? PrivateContact { get; init; }

    // Pasul 3 — Alte venituri
    public string? OtherIndependent { get; init; }
    public string? OtherIndependentRecords { get; init; }
    public string? OtherIncome { get; init; }
    public string? TaxPaymentsMade { get; init; }
    public string? CarriedLosses { get; init; }
    public string? CassOptIn { get; init; }
    public string? CrossBorder { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// Ce întrebări condiționate apar pentru PFA-ul ăsta, calculat din datele precompletate.
/// </summary>
/// <param name="AskPriorDocs">Există un interval din anul fiscal neacoperit de datele din RIDElance.</param>
/// <param name="PriorFrom">Începutul intervalului neacoperit.</param>
/// <param name="PriorTo">Sfârșitul intervalului neacoperit.</param>
/// <param name="AskCarriedLosses">PFA-ul e înființat înainte de anul fiscal, deci poate avea pierderi reportate.</param>
public sealed record FiscalProfileConditions(
    bool AskPriorDocs,
    DateOnly? PriorFrom,
    DateOnly? PriorTo,
    bool AskCarriedLosses);

/// <summary>Un câmp schimbat, pentru istoricul reviziilor.</summary>
public sealed record FiscalProfileFieldChange(string Field, string? OldValue, string? NewValue);
