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

        if (flags.CrossBorder)
        {
            return Finish(input, parameters, AllThree(TaxStatuses.RequiresClarification, TaxReasons.CrossBorder, ["crossBorderDetails"]), platformTaxes, []);
        }

        if (flags.OtherIndependent)
        {
            return Finish(
                input,
                parameters,
                AllThree(TaxStatuses.RequiresClarification, TaxReasons.OtherIndependentTotal, ["otherIndependentNetAnnual"]),
                platformTaxes,
                []);
        }

        var warnings = new List<string>();
        ComponentResult cas = Cas(n, flags, parameters, warnings);
        (ComponentResult cass, decimal cassDeductible) = Cass(n, flags, parameters);
        ComponentResult tax = IncomeTax(n, cas, cassDeductible, flags, parameters);

        return Finish(input, parameters, [cas, cass, tax], platformTaxes, warnings);
    }

    private static ComponentResult Cas(decimal n, ProfileFlags flags, TaxYearParameters p, List<string> warnings)
    {
        if (flags.PensionerFullYear || flags.OwnPensionSystem)
        {
            return Estimated(TaxComponents.Cas, 0, new Dictionary<string, object?>
            {
                ["N"] = n,
                ["exception"] = flags.PensionerFullYear ? "pensionerFullYear" : "ownPensionSystem",
            });
        }

        if (flags.PensionerMidYear)
        {
            return Clarify(TaxComponents.Cas, TaxReasons.PensionerMidYear, ["pensionerSince"]);
        }

        // Netul altor activități independente ar intra în N_CAS; cu ele, calculul e deja oprit mai sus.
        decimal nCas = n;
        decimal thresholdBase = p.CasThreshold24;
        if (nCas < p.CasThreshold12)
        {
            thresholdBase = 0;
        }
        else if (nCas < p.CasThreshold24)
        {
            thresholdBase = p.CasThreshold12;
        }

        decimal casBase = flags.CasVoluntaryBase is decimal voluntary ? Math.Max(thresholdBase, voluntary) : thresholdBase;

        if (IsNear(nCas, p.CasThreshold12) || IsNear(nCas, p.CasThreshold24))
        {
            warnings.Add(TaxWarnings.CasThresholdNear);
        }

        return Estimated(TaxComponents.Cas, Round(p.CasRate * casBase), new Dictionary<string, object?>
        {
            ["N_CAS"] = nCas,
            ["thresholdBase"] = thresholdBase,
            ["voluntaryBase"] = flags.CasVoluntaryBase,
            ["base"] = casBase,
            ["rate"] = p.CasRate,
        });
    }

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

        if (flags.CassOptIn)
        {
            return (Clarify(TaxComponents.Cass, TaxReasons.CassOptIn, ["cassOptInBase"]), deductible);
        }

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

        // Sub prag: excepțiile scot doar completarea până la minim, nu CASS pe profitul PFA.
        string? exception = CassMinimumException(flags);
        if (exception is not null)
        {
            breakdown["branch"] = "exception";
            breakdown["exception"] = exception;
            return (Estimated(TaxComponents.Cass, deductible, breakdown), deductible);
        }

        if (flags.PensionerMidYear)
        {
            return (Clarify(TaxComponents.Cass, TaxReasons.PensionerMidYear, ["pensionerSince"]), deductible);
        }

        if (flags.SalariedCassUnknown)
        {
            return (Clarify(TaxComponents.Cass, TaxReasons.CassExceptionUnknown, ["salaryAboveCassMin"]), deductible);
        }

        if (flags.OtherIncome)
        {
            return (Clarify(TaxComponents.Cass, TaxReasons.CassExceptionUnknown, ["otherIncomeCassBase"]), deductible);
        }

        breakdown["branch"] = "minimum";
        breakdown["minimumBase"] = p.CassMinThreshold;
        return (Estimated(TaxComponents.Cass, Round(p.CassRate * p.CassMinThreshold), breakdown), deductible);
    }

    private static string? CassMinimumException(ProfileFlags flags)
    {
        if (flags.SalariedCassExempt)
        {
            return "salariedCassExempt";
        }

        if (flags.PensionerFullYear)
        {
            return "pensionerFullYear";
        }

        return flags.StudentCassExempt ? "studentCassExempt" : null;
    }

    private static ComponentResult IncomeTax(decimal n, ComponentResult cas, decimal cassDeductible, ProfileFlags flags, TaxYearParameters p)
    {
        if (cas.Status != TaxStatuses.Estimated)
        {
            return cas with { Component = TaxComponents.IncomeTax, Breakdown = NoBreakdown };
        }

        if (flags.CarriedLosses)
        {
            return Clarify(TaxComponents.IncomeTax, TaxReasons.CarriedLosses, ["carriedLossesYear", "carriedLossesAmount"]);
        }

        // Se scade CASS deductibilă (10% din N), nu CASS completată la minim.
        decimal casAmount = cas.Amount ?? 0;
        decimal taxBase = Math.Max(0, n - casAmount - cassDeductible);
        return Estimated(TaxComponents.IncomeTax, Round(p.IncomeTaxRate * taxBase), new Dictionary<string, object?>
        {
            ["N"] = n,
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
            string status = three.All(c => c.Status == TaxStatuses.RuleUnavailable)
                ? TaxStatuses.RuleUnavailable
                : TaxStatuses.InsufficientData;
            reserve = new ReserveResult(status, null, null, null, missing, three[0].ReasonCode, assumedZero);
        }
        else if (input.Flags.TaxPaymentsMade && input.RecordedTaxPayments <= 0)
        {
            // PFA-ul spune că a plătit deja, dar plățile nu apar în evidență: o sumă de pus deoparte
            // fără ele ar fi prea mare.
            reserve = new ReserveResult(TaxStatuses.RequiresClarification, null, null, null, missing, TaxReasons.TaxPaymentsMissing, assumedZero);
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
