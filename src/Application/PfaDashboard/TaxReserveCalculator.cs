using System.Globalization;
using Application.PfaRegistrations;

namespace Application.PfaDashboard;

/// <summary>Rezerva fiscală a unei perioade, defalcată pe componente.</summary>
public sealed record TaxReserveResult(decimal Total, List<PfaTaxComponentResponse> Components);

/// <summary>
/// „Cât trebuie să pui deoparte" — singurul loc unde se calculează rezerva fiscală.
///
/// Stă separat de handler pentru că e singura bucată de aritmetică fiscală din dashboard care
/// nu depinde de bază: primește numere, întoarce numere. Asta o face testabilă, iar ea e
/// exact valoarea pe care spec-ul o cere identică pe Acasă și în Situație financiară.
///
/// Împărțirea pe două categorii e intenționată: TVA-ul intracomunitar și taxa de nerezident
/// se leagă direct de comisioanele perioadei, deci se calculează exact. Impozitul pe venit și
/// CAS/CASS sunt anuale prin natura lor — se estimează pe tot anul și se alocă perioadei
/// proporțional cu ponderea ei în venitul anual.
/// </summary>
public static class TaxReserveCalculator
{
    public static TaxReserveResult Compute(
        decimal platformFees,
        decimal boltFees,
        decimal periodNet,
        decimal annualIncome,
        PfaTaxCalculator.TaxResult annualTaxes,
        FiscalPolicyOptions fiscal)
    {
        ArgumentNullException.ThrowIfNull(annualTaxes);
        ArgumentNullException.ThrowIfNull(fiscal);

        decimal vatIntracom = fiscal.VatIntracomRate * platformFees;
        decimal boltNonResident = fiscal.BoltNonResidentRate * boltFees;

        // Plafonat la 1: o perioadă nu poate cântări mai mult decât anul din care face parte.
        // Fără plafon, o lună mai bună decât media ar trage impozitul anual peste total.
        decimal share = annualIncome > 0
            ? Math.Min(1m, periodNet / annualIncome)
            : 0m;

        decimal incomeTax = annualTaxes.IncomeTax * share;
        decimal casCass = (annualTaxes.Cas + annualTaxes.Cass) * share;

        List<PfaTaxComponentResponse> components =
        [
            new("vatIntracom",
                "TVA intracomunitar estimat",
                Round(vatIntracom),
                fiscal.VatIntracomRate,
                Round(platformFees),
                string.Format(CultureInfo.InvariantCulture, "{0:P0} din comisionul reținut de platforme", fiscal.VatIntracomRate)),
            new("boltNonResident",
                "Taxă nerezident Bolt",
                Round(boltNonResident),
                fiscal.BoltNonResidentRate,
                Round(boltFees),
                string.Format(CultureInfo.InvariantCulture, "{0:P0} din comisionul Bolt", fiscal.BoltNonResidentRate)),
            new("incomeTax",
                "Impozit pe venit estimat",
                Round(incomeTax),
                null,
                Round(annualTaxes.Profit),
                "Cota anuală estimată, alocată perioadei după ponderea ei în venitul anual"),
            new("casCass",
                "CAS/CASS estimat",
                Round(casCass),
                null,
                Round(annualTaxes.Profit),
                "Din pragurile fiscale ale anului, alocate perioadei după ponderea ei în venitul anual")
        ];

        return new TaxReserveResult(components.Sum(c => c.Amount), components);
    }

    /// <summary>Profitul real al perioadei: ce rămâne după cheltuieli și după rezerva fiscală.</summary>
    public static decimal RealProfit(decimal periodNet, decimal deductibleExpenses, decimal reserveTotal) =>
        periodNet - deductibleExpenses - reserveTotal;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
