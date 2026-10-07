namespace Application.FiscalEstimates;

public interface ITaxEngine
{
    TaxResult Calculate(TaxInput input, TaxYearParameters? parameters);
}

/// <summary>
/// Motorul de taxe estimate pentru sistemul real (spec SPEC_TAXE_ESTIMATE_PFA §6–§7): CAS, CASS,
/// impozit pe venit și „cât să pui deoparte”. Pur: fără IO, fără ceas, fără bază de date — tot ce
/// contează vine în <see cref="TaxInput"/>, iar parametrii anului din configurație.
/// </summary>
/// <remarks>
/// Fiecare componentă finală se rotunjește la leu, <see cref="MidpointRounding.AwayFromZero"/>;
/// impozitul se calculează din CAS și CASS deductibilă deja rotunjite. De confirmat de specialist.
/// TVA și impozitul nerezidenților nu intră (<c>NOT_CONFIGURED</c>, excluse din total).
/// </remarks>
public sealed class TaxEngine2026 : ITaxEngine
{
    private static readonly IReadOnlyDictionary<string, object?> NoBreakdown = new Dictionary<string, object?>();

    public TaxResult Calculate(TaxInput input, TaxYearParameters? parameters)
    {
        ArgumentNullException.ThrowIfNull(input);

        ComponentResult platformTaxes = new(TaxComponents.PlatformTaxes, TaxStatuses.NotConfigured, null, null, [], NoBreakdown);
        ProfileFlags flags = input.Flags;
        IncomeProjection projection = input.Projection;

        if (parameters is null)
        {
            return Finish(input, null, AllThree(TaxStatuses.RuleUnavailable, null, []), platformTaxes, []);
        }

        if (flags.PendingCorrection)
        {
            return Finish(input, parameters, AllThree(TaxStatuses.InsufficientData, TaxReasons.DataCorrectionPending, ["corectarea datelor PFA"]), platformTaxes, []);
        }

        if (projection.NetAnnualEstimated is not decimal projected)
        {
            return Finish(
                input,
                parameters,
                AllThree(TaxStatuses.InsufficientData, projection.UnavailableReason ?? TaxReasons.CoverageGap, projection.MissingInputs),
                platformTaxes,
                []);
        }

        decimal n = Math.Max(0, projected);

        var warnings = new List<string>();
        if (projection.UncoveredPeriod is not null)
        {
            warnings.Add(TaxWarnings.CoverageGap);
        }

        ComponentResult cas = Cas(n, flags, parameters, warnings);
        (ComponentResult cass, decimal cassDeductible) = Cass(n, flags, parameters);
        ComponentResult tax = IncomeTax(n, cas, cassDeductible, flags, parameters);

        return Finish(input, parameters, [cas, cass, tax], platformTaxes, warnings);
    }

    /// <summary>
    /// CAS: 0 sub 12 salarii minime, apoi pe baza de 12 sau 24 de salarii (art. 148). Pensionarul
    /// nu datorează CAS (art. 150 alin. (1)); studentul și angajatul da.
    /// </summary>
    private static ComponentResult Cas(decimal n, ProfileFlags flags, TaxYearParameters p, List<string> warnings)
    {
        if (flags.Pensioner)
        {
            return Estimated(TaxComponents.Cas, 0, new Dictionary<string, object?>
            {
                ["N"] = n,
                ["exception"] = "pensioner",
            });
        }

        decimal casBase = p.CasThreshold24;
        if (n < p.CasThreshold12)
        {
            casBase = 0;
        }
        else if (n < p.CasThreshold24)
        {
            casBase = p.CasThreshold12;
        }

        if (IsNear(n, p.CasThreshold12) || IsNear(n, p.CasThreshold24))
        {
            warnings.Add(TaxWarnings.CasThresholdNear);
        }

        return Estimated(TaxComponents.Cas, Round(p.CasRate * casBase), new Dictionary<string, object?>
        {
            ["N"] = n,
            ["base"] = casBase,
            ["rate"] = p.CasRate,
        });
    }

    /// <summary>
    /// CASS: 10% din net, plafonat la 72 de salarii (art. 170 alin. (1)). Sub 6 salarii se
    /// completează până la minim (art. 174 alin. (6)), cu excepția pensionarului, studentului și
    /// angajatului full-time (art. 174 alin. (7) și (8)). Deductibilul pentru impozit rămâne 10% din net.
    /// </summary>
    private static (ComponentResult Result, decimal Deductible) Cass(decimal n, ProfileFlags flags, TaxYearParameters p)
    {
        decimal baseCass = Math.Min(n, p.CassMaxBase);
        decimal deductible = Round(p.CassRate * baseCass);
        var breakdown = new Dictionary<string, object?>
        {
            ["N"] = n,
            ["base"] = baseCass,
            ["rate"] = p.CassRate,
            ["deductible"] = deductible,
        };

        // Pierdere sau net zero: nu se datorează CASS (art. 174 alin. (2)).
        if (n <= 0)
        {
            breakdown["branch"] = "zero";
            return (Estimated(TaxComponents.Cass, 0, breakdown), 0);
        }

        if (n >= p.CassMinThreshold)
        {
            breakdown["branch"] = "rate";
            return (Estimated(TaxComponents.Cass, deductible, breakdown), deductible);
        }

        string? exception = CassMinimumException(flags);
        if (exception is not null)
        {
            breakdown["branch"] = "exception";
            breakdown["exception"] = exception;
            return (Estimated(TaxComponents.Cass, deductible, breakdown), deductible);
        }

        breakdown["branch"] = "minimum";
        breakdown["minimumBase"] = p.CassMinThreshold;
        return (Estimated(TaxComponents.Cass, Round(p.CassRate * p.CassMinThreshold), breakdown), deductible);
    }

    private static string? CassMinimumException(ProfileFlags flags)
    {
        if (flags.EmployedFullTime)
        {
            return "employedFullTime";
        }

        if (flags.Pensioner)
        {
            return "pensioner";
        }

        return flags.Student ? "student" : null;
    }

    private static ComponentResult IncomeTax(decimal n, ComponentResult cas, decimal cassDeductible, ProfileFlags flags, TaxYearParameters p)
    {
        if (cas.Status != TaxStatuses.Estimated)
        {
            return cas with { Component = TaxComponents.IncomeTax, Breakdown = NoBreakdown };
        }

        if (flags.CarriedLosses && flags.CarriedLossesAmount is null)
        {
            return Clarify(TaxComponents.IncomeTax, TaxReasons.CarriedLosses, ["carriedLossesAmount"]);
        }

        // Pierderea reportată acoperă cel mult o parte din netul anului (70% din 2023).
        decimal losses = flags.CarriedLossesAmount is decimal carried ? Round(Math.Min(carried, p.CarriedLossOffsetLimit * n)) : 0;

        // Se scade CASS deductibilă (10% din N), nu CASS completată la minim.
        decimal casAmount = cas.Amount ?? 0;
        decimal taxBase = Math.Max(0, n - losses - casAmount - cassDeductible);
        return Estimated(TaxComponents.IncomeTax, Round(p.IncomeTaxRate * taxBase), new Dictionary<string, object?>
        {
            ["N"] = n,
            ["carriedLosses"] = losses,
            ["CAS"] = casAmount,
            ["CASS_deductible"] = cassDeductible,
            ["base"] = taxBase,
            ["rate"] = p.IncomeTaxRate,
        });
    }

    private static TaxResult Finish(
        TaxInput input,
        TaxYearParameters? parameters,
        List<ComponentResult> three,
        ComponentResult platformTaxes,
        IReadOnlyList<string> warnings)
    {
        var known = three.Where(c => c.Status == TaxStatuses.Estimated).ToList();
        var missing = three.Where(c => c.Status != TaxStatuses.Estimated).Select(c => c.Component).ToList();
        bool assumedZero = input.ExistingReserve is null;
        ReserveResult reserve;

        if (known.Count == 0)
        {
            // Rezerva spune același lucru ca cele trei componente, când spun toate același lucru:
            // „De clarificat” sus și jos, nu „Date insuficiente” peste o informație care lipsește.
            string status = TaxStatuses.InsufficientData;
            if (three.All(c => c.Status == TaxStatuses.RuleUnavailable))
            {
                status = TaxStatuses.RuleUnavailable;
            }
            else if (three.All(c => c.Status == TaxStatuses.RequiresClarification))
            {
                status = TaxStatuses.RequiresClarification;
            }
            reserve = new ReserveResult(status, null, null, null, missing, three[0].ReasonCode, assumedZero);
        }
        else
        {
            decimal annual = known.Sum(c => c.Amount ?? 0);
            decimal remaining = Math.Max(0, annual - input.RecordedTaxPayments);
            decimal toSetAside = Math.Max(0, remaining - (input.ExistingReserve ?? 0));
            decimal weekly = Round(toSetAside / Math.Max(1, input.WeeksLeftInYear));
            reserve = new ReserveResult(
                missing.Count == 0 ? TaxStatuses.Estimated : TaxStatuses.Partial,
                toSetAside,
                weekly,
                annual,
                missing,
                null,
                assumedZero);
        }

        return new TaxResult(parameters?.RuleVersion, [.. three, platformTaxes], reserve, warnings, input.Projection);
    }

    private static List<ComponentResult> AllThree(string status, string? reason, IReadOnlyList<string> missing) =>
    [
        new(TaxComponents.Cas, status, null, reason, missing, NoBreakdown),
        new(TaxComponents.Cass, status, null, reason, missing, NoBreakdown),
        new(TaxComponents.IncomeTax, status, null, reason, missing, NoBreakdown),
    ];

    private static ComponentResult Estimated(string component, decimal amount, IReadOnlyDictionary<string, object?> breakdown) =>
        new(component, TaxStatuses.Estimated, amount, null, [], breakdown);

    private static ComponentResult Clarify(string component, string reason, IReadOnlyList<string> missing) =>
        new(component, TaxStatuses.RequiresClarification, null, reason, missing, NoBreakdown);

    /// <summary>Între 90% și 100% dintr-un plafon CAS.</summary>
    private static bool IsNear(decimal value, decimal threshold) => value >= threshold * 0.9m && value < threshold;

    internal static decimal Round(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
