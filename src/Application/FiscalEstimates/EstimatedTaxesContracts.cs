namespace Application.FiscalEstimates;

public sealed record EstimatedTaxComponentResponse(
    string Component,
    string Status,
    decimal? Amount,
    string? ReasonCode,
    IReadOnlyList<string> MissingInputs,
    /// <summary>Doar pentru admin/contabilitate.</summary>
    IReadOnlyDictionary<string, object?>? Breakdown);

public sealed record EstimatedReserveResponse(
    string Status,
    decimal? Total,
    decimal? Weekly,
    decimal? AnnualEstimated,
    IReadOnlyList<string> Missing,
    string? ReasonCode,
    decimal? ExistingReserve,
    bool ExistingReserveAssumedZero,
    decimal RecordedTaxPayments);

public sealed record EstimatedProjectionResponse(
    decimal NetRealized,
    decimal? NetAnnualEstimated,
    decimal? WeeklyAverage,
    decimal WeeksUsed,
    decimal WeeksRemaining,
    /// <summary>Perioada fără date, estimată din media săptămânală; <c>null</c> = acoperire completă.</summary>
    string? UncoveredPeriod,
    decimal UncoveredWeeks);

/// <summary>
/// Plafoanele anului față de cifrele PFA-ului: încasările de până acum față de plafonul de scutire
/// TVA, netul anual estimat față de plafoanele CAS (12 și 24 de salarii minime).
/// </summary>
public sealed record EstimatedThresholdsResponse(
    decimal IncomeYtd,
    decimal? NetAnnualEstimated,
    decimal VatThreshold,
    decimal? Cas12,
    decimal? Cas24);

public sealed record EstimatedTaxesRunSummary(
    Guid Id,
    DateTime CreatedAtUtc,
    string Status,
    bool Stale,
    int ProfileRevision,
    string? RuleVersion,
    DateOnly AsOf);

/// <summary>
/// Răspunsul „Taxe estimate” (spec §11.1). Cu profilul necompletat: doar <c>Locked</c> și statusul
/// profilului, nimic altceva.
/// </summary>
public sealed record EstimatedTaxesResponse(
    int TaxYear,
    bool Locked,
    string? ProfileStatus,
    DateOnly? AsOf = null,
    string? Status = null,
    bool Stale = false,
    EstimatedReserveResponse? Reserve = null,
    IReadOnlyList<EstimatedTaxComponentResponse>? Components = null,
    IReadOnlyList<string>? Warnings = null,
    EstimatedProjectionResponse? Projection = null,
    // Doar pentru admin/contabilitate.
    int? ProfileRevision = null,
    string? RuleVersion = null,
    Guid? FinancialSnapshotId = null,
    IReadOnlyList<EstimatedTaxesRunSummary>? Runs = null,
    EstimatedThresholdsResponse? Thresholds = null);
