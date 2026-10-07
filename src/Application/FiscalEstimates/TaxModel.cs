using System.Text.Json.Serialization;

namespace Application.FiscalEstimates;

/// <summary>Parametrii fiscali ai unui an, din configurația versionată (<c>Parameters/tax-YYYY.json</c>).</summary>
public sealed record TaxYearParameters(
    [property: JsonPropertyName("tax_year")] int TaxYear,
    [property: JsonPropertyName("rule_version")] string RuleVersion,
    [property: JsonPropertyName("min_wage_reference")] decimal MinWageReference,
    [property: JsonPropertyName("cass_min_threshold")] decimal CassMinThreshold,
    [property: JsonPropertyName("cas_threshold_12")] decimal CasThreshold12,
    [property: JsonPropertyName("cas_threshold_24")] decimal CasThreshold24,
    [property: JsonPropertyName("cass_max_base")] decimal CassMaxBase,
    [property: JsonPropertyName("cas_rate")] decimal CasRate,
    [property: JsonPropertyName("cass_rate")] decimal CassRate,
    [property: JsonPropertyName("income_tax_rate")] decimal IncomeTaxRate,
    [property: JsonPropertyName("source")] string Source,
    // Cât din netul anului pot acoperi pierderile reportate (70% din 2023). De confirmat de specialist.
    [property: JsonPropertyName("carried_loss_offset_limit")] decimal CarriedLossOffsetLimit);

public static class TaxComponents
{
    public const string Cas = "CAS";
    public const string Cass = "CASS";
    public const string IncomeTax = "INCOME_TAX";
    public const string Reserve = "RESERVE";
    public const string PlatformTaxes = "PLATFORM_TAXES";
}

public static class TaxStatuses
{
    public const string Calculating = "CALCULATING";
    public const string Estimated = "ESTIMATED";
    public const string InsufficientData = "INSUFFICIENT_DATA";
    public const string RequiresClarification = "REQUIRES_CLARIFICATION";
    public const string RuleUnavailable = "RULE_UNAVAILABLE";
    public const string NotConfigured = "NOT_CONFIGURED";
    public const string Error = "ERROR";
    public const string Partial = "PARTIAL";
}

public static class TaxReasons
{
    public const string CoverageGap = "COVERAGE_GAP";
    public const string ShortHistory = "SHORT_HISTORY";
    public const string DataCorrectionPending = "DATA_CORRECTION_PENDING";
    public const string CarriedLosses = "CARRIED_LOSSES";
}

public static class TaxWarnings
{
    public const string CasThresholdNear = "CAS_THRESHOLD_NEAR";

    /// <summary>O perioadă fără date a fost estimată din media săptămânilor cunoscute.</summary>
    public const string CoverageGap = "COVERAGE_GAP";
}

/// <summary>
/// Situația fiscală a PFA-ului, din profilul simplificat: pensionar, student, angajat full-time.
/// Cumulative — un pensionar angajat are ambele excepții.
/// </summary>
/// <remarks>
/// Ce schimbă fiecare (Codul fiscal):
/// <list type="bullet">
/// <item>Pensionar: nu datorează CAS (art. 150 alin. (1)); CASS fără completarea până la 6 salarii (art. 174 alin. (7) lit. c)).</item>
/// <item>Student sub 26 de ani: CASS 10% din net, fără completare (art. 154 alin. (1) lit. a), art. 174 alin. (8) lit. a)); CAS ca oricine.</item>
/// <item>Angajat full-time: CASS fără completare, salariul trecând de 6 salarii minime (art. 174 alin. (7) lit. a)); CAS ca oricine (art. 150 alin. (2) scutește doar drepturile de autor).</item>
/// </list>
/// </remarks>
public sealed record ProfileFlags
{
    public bool Pensioner { get; init; }
    public bool Student { get; init; }
    public bool EmployedFullTime { get; init; }
    public bool PendingCorrection { get; init; }

    /// <summary>Pierderea reportată, doar din istoricul D212 (motorul anual); profilul nu o mai cere.</summary>
    public bool CarriedLosses { get; init; }
    public decimal? CarriedLossesAmount { get; init; }
}

/// <summary>
/// Netul anual estimat, sau de ce nu se poate estima. Vine din <see cref="IncomeProjector"/>.
/// </summary>
/// <param name="UncoveredPeriod">Perioada fără date, estimată din medie („01.01.2026 – 30.04.2026”); <c>null</c> = acoperire completă.</param>
/// <param name="UncoveredWeeks">Câte săptămâni are perioada fără date.</param>
public sealed record IncomeProjection(
    decimal? NetAnnualEstimated,
    string? UnavailableReason,
    IReadOnlyList<string> MissingInputs,
    decimal NetRealized,
    decimal? WeeklyAverage,
    decimal WeeksUsed,
    decimal WeeksRemaining,
    string? UncoveredPeriod = null,
    decimal UncoveredWeeks = 0);

/// <summary>Tot ce intră în motor, imutabil. Nicio dată nu se citește din afara lui.</summary>
public sealed record TaxInput(
    IncomeProjection Projection,
    ProfileFlags Flags,
    decimal RecordedTaxPayments,
    decimal? ExistingReserve,
    int WeeksLeftInYear);

public sealed record ComponentResult(
    string Component,
    string Status,
    decimal? Amount,
    string? ReasonCode,
    IReadOnlyList<string> MissingInputs,
    IReadOnlyDictionary<string, object?> Breakdown);

public sealed record ReserveResult(
    string Status,
    decimal? Total,
    decimal? Weekly,
    decimal? AnnualEstimated,
    IReadOnlyList<string> Missing,
    string? ReasonCode,
    bool ExistingReserveAssumedZero);

public sealed record TaxResult(
    string? RuleVersion,
    IReadOnlyList<ComponentResult> Components,
    ReserveResult Reserve,
    IReadOnlyList<string> Warnings,
    IncomeProjection Projection);
