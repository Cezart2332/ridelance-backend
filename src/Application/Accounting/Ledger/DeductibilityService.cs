using System.Text.RegularExpressions;
using Domain.Accounting;

namespace Application.Accounting.Ledger;

/// <summary>
/// Regulile de clasificare și deductibilitate valabile pentru un PFA: categoriile
/// (<see cref="ExpenseCategoryRule"/>, cu toate versiunile) și istoricul setării
/// <c>vehicle_deductibility</c>.
/// </summary>
public sealed record LedgerRules(IReadOnlyList<ExpenseCategoryRule> Categories, IReadOnlyList<PfaAccountingSetting> VehicleDeductibility);

/// <summary>
/// Clasificarea deterministă și deductibilitatea unei cheltuieli (spec contabilitate B6), funcții
/// pure: regula și setarea se aleg la <b>data cheltuielii</b>, nu la data de azi.
/// </summary>
public static class DeductibilityService
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>Categoria găsită după contrapartidă (sau detaliile plății), valabilă la <paramref name="date"/>.</summary>
    public static ExpenseCategoryRule? Classify(IEnumerable<ExpenseCategoryRule> categories, DateOnly date, params string?[] texts)
    {
        string haystack = string.Join(' ', texts.Where(text => !string.IsNullOrWhiteSpace(text)));
        if (haystack.Length == 0)
        {
            return null;
        }

        return categories
            .Where(rule => rule.CounterpartyPattern is { Length: > 0 } && IsValidAt(rule, date))
            .OrderBy(rule => rule.Category, StringComparer.Ordinal)
            .FirstOrDefault(rule => Regex.IsMatch(haystack, rule.CounterpartyPattern!, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, PatternTimeout));
    }

    /// <summary>
    /// Completează deductibilitatea înregistrării: <c>SPECIAL_RULE</c> (amortizare etc., DE CONFIRMAT)
    /// fără procent; categoriile auto după setarea <c>vehicle_deductibility</c> de la data
    /// cheltuielii; restul după regula categoriei. Veniturile și cheltuielile fără categorie rămân fără.
    /// </summary>
    public static void Resolve(LedgerEntry entry, LedgerRules rules)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(rules);
        entry.VehicleRelated = false;
        entry.DeductibilityType = null;
        entry.DeductiblePercent = null;
        entry.DeductibleAmount = null;
        entry.DeductibilitySettingId = null;
        entry.DeductibilityRuleId = null;
        entry.DeductibilityValidFrom = null;

        if (entry.TransactionType != LedgerTransactionType.Expense || entry.Category is null)
        {
            return;
        }

        ExpenseCategoryRule? rule = rules.Categories
            .Where(r => r.Category == entry.Category && IsValidAt(r, entry.Date))
            .OrderByDescending(r => r.ValidFrom)
            .FirstOrDefault();
        if (rule is null)
        {
            return;
        }

        entry.VehicleRelated = rule.VehicleRelated;
        entry.DeductibilityRuleId = rule.Id;
        if (rule.DefaultDeductibility == DeductibilityType.SpecialRule)
        {
            entry.DeductibilityType = DeductibilityType.SpecialRule;
            entry.DeductibilityValidFrom = rule.ValidFrom;
            return;
        }

        if (rule.VehicleRelated)
        {
            PfaAccountingSetting? setting = rules.VehicleDeductibility
                .Where(s => s.ValidFrom <= entry.Date)
                .OrderByDescending(s => s.ValidFrom)
                .ThenByDescending(s => s.ChangedAtUtc)
                .FirstOrDefault();
            DeductibilityType? type = setting is null ? null : AccountingJson.Deserialize<DeductibilityType?>(setting.ValueJson, null);
            if (setting is null || type is null)
            {
                // Fără setare la data cheltuielii: procentul auto nu se ghicește.
                return;
            }

            Apply(entry, type.Value, setting.ValidFrom);
            entry.DeductibilitySettingId = setting.Id;
            return;
        }

        Apply(entry, rule.DefaultDeductibility, rule.ValidFrom);
    }

    public static decimal? PercentOf(DeductibilityType type) => type switch
    {
        DeductibilityType.Percent100 => 100m,
        DeductibilityType.Percent50 => 50m,
        DeductibilityType.NonDeductible => 0m,
        _ => null,
    };

    private static void Apply(LedgerEntry entry, DeductibilityType type, DateOnly validFrom)
    {
        entry.DeductibilityType = type;
        entry.DeductibilityValidFrom = validFrom;
        entry.DeductiblePercent = PercentOf(type);
        entry.DeductibleAmount = entry.DeductiblePercent is { } percent
            ? Math.Round(Math.Abs(entry.Amount) * percent / 100, 2, MidpointRounding.AwayFromZero)
            : null;
    }

    private static bool IsValidAt(ExpenseCategoryRule rule, DateOnly date) =>
        rule.ValidFrom <= date && (rule.ValidTo is null || date <= rule.ValidTo);
}
