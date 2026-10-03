using System.Globalization;

namespace Application.Accounting.Tax;

/// <summary>
/// Termenul de depunere din regula <c>Deadline</c> (spec declarații §2): <c>MONTHLY:25</c> = ziua 25
/// a lunii următoare perioadei; <c>ANNUAL:05-25</c> = 25 mai a anului următor; <c>ANNUAL:02-LAST</c>
/// = ultima zi a lui februarie din anul următor.
/// </summary>
public static class DeclarationDeadline
{
    /// <param name="period"><c>yyyy-MM</c> pentru lunare, <c>yyyy</c> sau <c>yyyy-MM</c> pentru anuale.</param>
    public static DateOnly Of(string formula, string period)
    {
        ArgumentNullException.ThrowIfNull(formula);
        ArgumentNullException.ThrowIfNull(period);
        int year = int.Parse(period[..4], CultureInfo.InvariantCulture);
        string[] parts = formula.Split(':', 2);
        if (parts.Length != 2)
        {
            throw new TaxRuleConfigurationException($"Termen invalid: {formula}.");
        }

        switch (parts[0].ToUpperInvariant())
        {
            case "MONTHLY":
                int month = int.Parse(period[5..7], CultureInfo.InvariantCulture);
                DateOnly next = new DateOnly(year, month, 1).AddMonths(1);
                return new DateOnly(next.Year, next.Month, Math.Min(int.Parse(parts[1], CultureInfo.InvariantCulture), DateTime.DaysInMonth(next.Year, next.Month)));
            case "ANNUAL":
                string[] date = parts[1].Split('-');
                int dueMonth = int.Parse(date[0], CultureInfo.InvariantCulture);
                int days = DateTime.DaysInMonth(year + 1, dueMonth);
                int day = date[1].Equals("LAST", StringComparison.OrdinalIgnoreCase) ? days : Math.Min(int.Parse(date[1], CultureInfo.InvariantCulture), days);
                return new DateOnly(year + 1, dueMonth, day);
            default:
                throw new TaxRuleConfigurationException($"Termen invalid: {formula}.");
        }
    }
}
