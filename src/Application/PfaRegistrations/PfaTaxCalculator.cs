using Application.FiscalEstimates;

namespace Application.PfaRegistrations;

/// <summary>
/// Romanian PFA real-system tax calculator.
/// Encodes the 2025/2026 formula for CAS, CASS, and income tax.
/// </summary>
public static class PfaTaxCalculator
{
    public sealed record TaxResult(
        decimal Profit,
        decimal Cas,
        decimal Cass,
        decimal IncomeTax,
        decimal TotalTax,
        decimal NetIncome);

    public sealed record TaxThresholdProgress(
        decimal Profit,
        decimal CasFirstThreshold,
        decimal CasSecondThreshold,
        decimal CassFirstThreshold,
        decimal CassMaximumThreshold,
        decimal RemainingToNextCasThreshold,
        decimal RemainingToNextCassThreshold,
        bool HasReachedCasFirstThreshold,
        bool HasReachedCasSecondThreshold,
        bool HasReachedCassFirstThreshold,
        bool HasReachedCassMaximumThreshold);

    /// <summary>
    /// Computes all PFA taxes for the given year.
    /// </summary>
    /// <param name="annualIncome">Total annual gross income (RON)</param>
    /// <param name="deductibleExpenses">Total verified deductible expenses for the year (RON)</param>
    /// <param name="year">Tax year (used to select the minimum gross salary reference)</param>
    /// <param name="parameters">
    /// Plafoanele anului — aceleași ca ale motorului de estimări, inclusiv ce a schimbat adminul.
    /// Fără ele, calculul rămâne pe valorile legale cunoscute la scriere.
    /// </param>
    public static TaxResult Compute(decimal annualIncome, decimal deductibleExpenses, int year, TaxYearParameters? parameters = null)
    {
        Limits l = LimitsFor(year, parameters);

        decimal profit = Math.Max(0m, annualIncome - deductibleExpenses);

        // ── CAS (25%) ──────────────────────────────────────────────────────────
        decimal cas;
        if (profit >= l.Cas24)
        {
            cas = l.CasRate * l.Cas24; // capped at 2 years salary
        }
        else if (profit >= l.Cas12)
        {
            cas = l.CasRate * l.Cas12; // capped at 1 year salary
        }
        else
        {
            cas = 0m; // below threshold → no CAS
        }

        // ── CASS (10%) ─────────────────────────────────────────────────────────
        decimal cass;
        if (profit < l.CassMin)
        {
            cass = l.CassRate * l.CassMin; // minimum contribution (6 salaries)
        }
        else if (profit >= l.CassMax)
        {
            cass = l.CassRate * l.CassMax; // maximum cap (72 salaries)
        }
        else
        {
            cass = l.CassRate * profit;
        }

        // ── Impozit pe venit (10%) ─────────────────────────────────────────────
        // Both CAS and CASS are fully deductible from the taxable base
        decimal taxableBase = Math.Max(0m, profit - cas - cass);
        decimal incomeTax = l.IncomeTaxRate * taxableBase;

        decimal totalTax = cas + cass + incomeTax;
        decimal netIncome = annualIncome - deductibleExpenses - totalTax;

        return new TaxResult(
            Profit: Math.Round(profit, 2),
            Cas: Math.Round(cas, 2),
            Cass: Math.Round(cass, 2),
            IncomeTax: Math.Round(incomeTax, 2),
            TotalTax: Math.Round(totalTax, 2),
            NetIncome: Math.Round(netIncome, 2));
    }

    public static TaxThresholdProgress ComputeThresholdProgress(
        decimal annualIncome,
        decimal deductibleExpenses,
        int year,
        TaxYearParameters? parameters = null)
    {
        Limits l = LimitsFor(year, parameters);
        decimal profit = Math.Max(0m, annualIncome - deductibleExpenses);

        decimal casFirstThreshold = l.Cas12;
        decimal casSecondThreshold = l.Cas24;
        decimal cassFirstThreshold = l.CassMin;
        decimal cassMaximumThreshold = l.CassMax;

        decimal remainingToNextCasThreshold = 0m;
        if (profit < casFirstThreshold)
        {
            remainingToNextCasThreshold = casFirstThreshold - profit;
        }
        else if (profit < casSecondThreshold)
        {
            remainingToNextCasThreshold = casSecondThreshold - profit;
        }

        decimal remainingToNextCassThreshold = 0m;
        if (profit < cassFirstThreshold)
        {
            remainingToNextCassThreshold = cassFirstThreshold - profit;
        }
        else if (profit < cassMaximumThreshold)
        {
            remainingToNextCassThreshold = cassMaximumThreshold - profit;
        }

        return new TaxThresholdProgress(
            Profit: Math.Round(profit, 2),
            CasFirstThreshold: Math.Round(casFirstThreshold, 2),
            CasSecondThreshold: Math.Round(casSecondThreshold, 2),
            CassFirstThreshold: Math.Round(cassFirstThreshold, 2),
            CassMaximumThreshold: Math.Round(cassMaximumThreshold, 2),
            RemainingToNextCasThreshold: Math.Round(remainingToNextCasThreshold, 2),
            RemainingToNextCassThreshold: Math.Round(remainingToNextCassThreshold, 2),
            HasReachedCasFirstThreshold: profit >= casFirstThreshold,
            HasReachedCasSecondThreshold: profit >= casSecondThreshold,
            HasReachedCassFirstThreshold: profit >= cassFirstThreshold,
            HasReachedCassMaximumThreshold: profit >= cassMaximumThreshold);
    }

    private sealed record Limits(
        decimal Cas12,
        decimal Cas24,
        decimal CassMin,
        decimal CassMax,
        decimal CasRate,
        decimal CassRate,
        decimal IncomeTaxRate);

    private static Limits LimitsFor(int year, TaxYearParameters? p)
    {
        if (p is not null)
        {
            return new Limits(p.CasThreshold12, p.CasThreshold24, p.CassMinThreshold, p.CassMaxBase, p.CasRate, p.CassRate, p.IncomeTaxRate);
        }

        // Romanian minimum gross salary (brut) — 4050 RON/month from January 2025+
        decimal grossSalary = year >= 2025 ? 4050m : 3300m;
        return new Limits(grossSalary * 12m, grossSalary * 24m, grossSalary * 6m, grossSalary * 72m, 0.25m, 0.10m, 0.10m);
    }
}
