namespace Application.FiscalProfiles;

/// <summary>O valoare precompletată: ce e, de unde vine și când a fost citită.</summary>
public sealed record FiscalProfileFact<T>(T? Value, string Source, DateTime? ObservedAtUtc);

/// <summary>Datele din contul PFA-ului, afișate read-only la pasul 1.</summary>
public sealed record FiscalProfileFacts(
    string? PfaName,
    string? Cui,
    FiscalProfileFact<DateOnly?> PfaRegisteredOn,
    FiscalProfileFact<DateOnly?> ActivityStartedOn,
    FiscalProfileFact<DateTime?> AccessGrantedAt,
    string Regime);

public sealed record FiscalProfileActor(Guid UserId, string Name, string Role);

public sealed record DataCorrectionResponse(
    Guid Id,
    string Fields,
    string Details,
    string State,
    DateTime CreatedAtUtc,
    DateTime? ResolvedAtUtc);

public sealed record FiscalProfileResponse(
    Guid Id,
    Guid PfaRegistrationId,
    int TaxYear,
    string Regime,
    /// <summary><c>NOT_STARTED</c>, <c>DRAFT</c> sau <c>COMPLETED</c>.</summary>
    string Status,
    FiscalProfileAnswers Answers,
    int Revision,
    DateTime? FirstPromptShownAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? EstimatedTaxesUnlockedAtUtc,
    DateTime UpdatedAtUtc,
    FiscalProfileActor? LastChangedBy,
    FiscalProfileFacts Facts,
    FiscalProfileConditions Conditions,
    /// <summary>Cererile de corectare deschise. Doar pentru staff; PFA-ul le vede ca listă goală.</summary>
    IReadOnlyList<DataCorrectionResponse> Corrections);

public sealed record FiscalProfileRevisionResponse(
    int Revision,
    DateTime CreatedAtUtc,
    FiscalProfileActor Actor,
    IReadOnlyList<FiscalProfileFieldChange> Changes,
    string? Reason);

/// <summary>
/// Răspunsul pentru estimări. Cu profilul necompletat e doar <c>locked</c>: frontendul arată
/// invitația și nu cere nimic altceva.
/// </summary>
public sealed record EstimatedTaxesStatusResponse(
    int TaxYear,
    bool Locked,
    string ProfileStatus,
    DateTime? UnlockedAtUtc);

/// <summary>Cine face o modificare, din ce dashboard.</summary>
public enum FiscalProfileScope
{
    Pfa,
    Admin,
    Accounting,
}
