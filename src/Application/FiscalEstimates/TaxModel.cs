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
    [property: JsonPropertyName("source")] string Source);

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
    public const string PensionerMidYear = "PENSIONER_MID_YEAR";
    public const string OtherIndependentTotal = "OTHER_INDEPENDENT_TOTAL";
    public const string CassExceptionUnknown = "CASS_EXCEPTION_UNKNOWN";
    public const string CassOptIn = "CASS_OPT_IN";
    public const string CarriedLosses = "CARRIED_LOSSES";
    public const string CrossBorder = "CROSS_BORDER";
    public const string TaxPaymentsMissing = "TAX_PAYMENTS_MISSING";
}

public static class TaxWarnings
{
    public const string CasThresholdNear = "CAS_THRESHOLD_NEAR";
}

/// <summary>Flagurile din profilul fiscal care schimbă calculul (spec §5). Cumulative, per contribuție.</summary>
public sealed record ProfileFlags
{
    public bool PensionerFullYear { get; init; }
    public bool PensionerMidYear { get; init; }
    public bool OwnPensionSystem { get; init; }
    public bool SalariedCassExempt { get; init; }

    /// <summary>Salariat, dar nu știm dacă salariul trece de pragul CASS (răspuns lipsă pe profil vechi).</summary>
    public bool SalariedCassUnknown { get; init; }

    public bool StudentCassExempt { get; init; }
    public bool OtherIncome { get; init; }
    public bool OtherIndependent { get; init; }
    public decimal? CasVoluntaryBase { get; init; }
    public bool CassOptIn { get; init; }
    public bool CarriedLosses { get; init; }
    public bool CrossBorder { get; init; }
    public bool TaxPaymentsMade { get; init; }
    public bool PendingCorrection { get; init; }
}

/// <summary>
/// Netul anual estimat, sau de ce nu se poate estima. Vine din <see cref="IncomeProjector"/>.
/// </summary>
public sealed record IncomeProjection(
    decimal? NetAnnualEstimated,
    string? UnavailableReason,
    IReadOnlyList<string> MissingInputs,
    decimal NetRealized,
    decimal? WeeklyAverage,
    decimal WeeksUsed,
    decimal WeeksRemaining);

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
