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

        // Fără netul celorlalte activități nu știm plafonul CAS (se aplică pe total), deci nimic.
        if (flags.OtherIndependent && flags.OtherIndependentNetAnnual is null)
        {
            return Finish(
                input,
                parameters,
                AllThree(TaxStatuses.RequiresClarification, TaxReasons.OtherIndependentTotal, ["otherIndependentNetAnnual"]),
                platformTaxes,
                []);
        }

        decimal otherNet = flags.OtherIndependent ? Math.Max(0, flags.OtherIndependentNetAnnual ?? 0) : 0;

        var warnings = new List<string>();
        if (projection.UncoveredPeriod is not null)
        {
            warnings.Add(TaxWarnings.CoverageGap);
        }

        ComponentResult cas = Cas(n, otherNet, flags, parameters, warnings);
        (ComponentResult cass, decimal cassDeductible) = Cass(n, otherNet, flags, parameters);
        ComponentResult tax = IncomeTax(n, cas, cassDeductible, flags, parameters);

        return Finish(input, parameters, [cas, cass, tax], platformTaxes, warnings);
    }

    /// <param name="otherNet">Netul anual al celorlalte activități independente (0 dacă nu are).</param>
    private static ComponentResult Cas(decimal n, decimal otherNet, ProfileFlags flags, TaxYearParameters p, List<string> warnings)
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

        // Plafoanele CAS se aplică pe tot netul din activități independente, nu doar pe PFA (spec §6).
        decimal nCas = n + otherNet;
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
            ["N"] = n,
            ["otherIndependentNet"] = otherNet,
            ["N_CAS"] = nCas,
            ["thresholdBase"] = thresholdBase,
            ["voluntaryBase"] = flags.CasVoluntaryBase,
            ["base"] = casBase,
            ["rate"] = p.CasRate,
        });
    }

    /// <summary>
    /// CASS pe profitul PFA, apoi opțiunea de plată CASS, dacă PFA-ul a ales-o: se plătește cea mai
    /// mare dintre cele două. Deductibilul (pentru impozit) rămâne 10% din N.
    /// </summary>
    private static (ComponentResult Result, decimal Deductible) Cass(decimal n, decimal otherNet, ProfileFlags flags, TaxYearParameters p)
    {
        (ComponentResult result, decimal deductible) = CassOnProfit(n, otherNet, flags, p);
        if (!flags.CassOptIn)
        {
            return (result, deductible);
        }

        if (flags.CassOptInBase is not decimal optInBase)
        {
            return (Clarify(TaxComponents.Cass, TaxReasons.CassOptIn, ["cassOptInBase"]), deductible);
        }

        if (result.Status != TaxStatuses.Estimated)
        {
            return (result, deductible);
        }

        decimal optInAmount = Round(p.CassRate * Math.Min(optInBase, p.CassMaxBase));
        var breakdown = new Dictionary<string, object?>(result.Breakdown) { ["optInBase"] = optInBase };
        if (optInAmount <= (result.Amount ?? 0))
        {
            return (result with { Breakdown = breakdown }, deductible);
        }

        breakdown["branch"] = "optIn";
        return (Estimated(TaxComponents.Cass, optInAmount, breakdown), deductible);
    }

    private static (ComponentResult Result, decimal Deductible) CassOnProfit(decimal n, decimal otherNet, ProfileFlags flags, TaxYearParameters p)
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

        if (n <= 0)
        {
            breakdown["branch"] = "zero";
            return (Estimated(TaxComponents.Cass, 0, breakdown), 0);
        }

        // Minimul se completează o singură dată, pe tot netul din activități independente: cu
        // celelalte activități peste prag, PFA-ul plătește doar 10% din profitul lui.
        if (n + otherNet >= p.CassMinThreshold)
        {
            breakdown["branch"] = n >= p.CassMinThreshold ? "rate" : "combined";
            breakdown["otherIndependentNet"] = otherNet;
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

        if (flags.OtherIncome && flags.OtherIncomeCassInsured is null)
        {
            return (Clarify(TaxComponents.Cass, TaxReasons.CassExceptionUnknown, ["otherIncomeCassInsured"]), deductible);
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

        if (flags.OtherIncome && flags.OtherIncomeCassInsured == true)
        {
            return "otherIncomeCassInsured";
        }

        return flags.StudentCassExempt ? "studentCassExempt" : null;
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
