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

    /// <summary>
    /// Computes all PFA taxes for the given year.
    /// </summary>
    /// <param name="annualIncome">Total annual gross income (RON)</param>
    /// <param name="deductibleExpenses">Total verified deductible expenses for the year (RON)</param>
    /// <param name="year">Tax year (used to select the minimum gross salary reference)</param>
    public static TaxResult Compute(decimal annualIncome, decimal deductibleExpenses, int year)
    {
        // Romanian minimum gross salary (brut) — 4050 RON/month from January 2025+
        decimal grossSalary = year >= 2025 ? 4050m : 3300m;

        decimal profit = Math.Max(0m, annualIncome - deductibleExpenses);

        // ── CAS (25%) ──────────────────────────────────────────────────────────
        decimal cas;
        if (profit >= grossSalary * 24m)
        {
            cas = 0.25m * grossSalary * 24m; // capped at 2 years salary
        }
        else if (profit >= grossSalary * 12m)
        {
            cas = 0.25m * grossSalary * 12m; // capped at 1 year salary
        }
        else
        {
            cas = 0m; // below threshold → no CAS
        }

        // ── CASS (10%) ─────────────────────────────────────────────────────────
        decimal cass;
        if (profit < grossSalary * 6m)
        {
            cass = 0.10m * grossSalary * 6m; // minimum contribution (6 salaries)
        }
        else if (profit >= grossSalary * 72m)
        {
            cass = 0.10m * grossSalary * 72m; // maximum cap (72 salaries)
        }
        else
        {
            cass = 0.10m * profit;
        }

        // ── Impozit pe venit (10%) ─────────────────────────────────────────────
        // Both CAS and CASS are fully deductible from the taxable base
        decimal taxableBase = Math.Max(0m, profit - cas - cass);
        decimal incomeTax = 0.10m * taxableBase;

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
}
