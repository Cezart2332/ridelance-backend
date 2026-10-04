using System.Text.RegularExpressions;
using Domain.Accounting;

namespace Application.Accounting.Ledger;

/// <summary>
/// Regulile de clasificare și deductibilitate valabile pentru un PFA: categoriile
/// (<see cref="ExpenseCategoryRule"/>, cu toate versiunile), istoricul setării
/// <c>vehicle_deductibility</c> și pragul de mijloc fix (<see cref="FixedAssetRule"/>).
/// </summary>
public sealed record LedgerRules(
    IReadOnlyList<ExpenseCategoryRule> Categories,
    IReadOnlyList<PfaAccountingSetting> VehicleDeductibility,
    IReadOnlyList<FixedAssetRule>? FixedAssets = null)
{
    /// <summary>Regula de mijloc fix valabilă la <paramref name="date"/>.</summary>
    public FixedAssetRule? FixedAssetRuleAt(DateOnly date) =>
        FixedAssets?
            .Where(r => r.ValidFrom <= date && (r.ValidTo is null || date <= r.ValidTo))
            .OrderByDescending(r => r.ValidFrom)
            .FirstOrDefault();
}

/// <summary>
/// Clasificarea deterministă și deductibilitatea unei cheltuieli (spec contabilitate B6), funcții
/// pure: regula și setarea se aleg la <b>data cheltuielii</b>, nu la data de azi.
/// </summary>
public static class DeductibilityService
{
    public const string NonRecoverableVatCategory = "NON_RECOVERABLE_VAT";
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
    /// Completează deductibilitatea înregistrării, pe partea din activitate (fără partea personală): <c>SPECIAL_RULE</c> (amortizare etc., DE CONFIRMAT)
    /// fără procent; categoriile auto după setarea <c>vehicle_deductibility</c> de la data
    /// cheltuielii (50% până la prima setare); restul după regula categoriei. Veniturile și cheltuielile fără categorie rămân fără.
    /// </summary>
    public static void Resolve(LedgerEntry entry, LedgerRules rules)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(rules);
        ResolveCategory(entry, rules);
        ApplyFixedAssetReview(entry, rules);
    }

    /// <summary>
    /// Achizițiile care pot fi mijloace fixe (spec registre §6): peste prag, cu natură care nu e de
    /// consum. Până la decizia Adminului, și după decizia „mijloc fix”, plata nu se deduce: în REF
    /// intră doar amortizarea. Decizia Adminului nu se mai schimbă la o reclasificare.
    /// </summary>
    private static void ApplyFixedAssetReview(LedgerEntry entry, LedgerRules rules)
    {
        if (entry.FixedAssetReview is FixedAssetReview.None or FixedAssetReview.Pending)
        {
            FixedAssetRule? rule = rules.FixedAssetRuleAt(entry.DocumentDate ?? entry.Date);
            bool candidate = entry.TransactionType == LedgerTransactionType.Expense &&
                entry.Category != NonRecoverableVatCategory &&
                entry.StornoOfEntryId is null &&
                rule is not null &&
                !rule.Excludes(entry.Category) &&
                entry.BusinessAmount >= rule.Threshold;
            entry.FixedAssetReview = candidate ? FixedAssetReview.Pending : FixedAssetReview.None;
        }

        if (entry.FixedAssetReview is FixedAssetReview.Pending or FixedAssetReview.FixedAsset)
        {
            entry.DeductibleAmount = 0m;
        }
    }

    private static void ResolveCategory(LedgerEntry entry, LedgerRules rules)
    {
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
                // Fără setare la data cheltuielii: 50%, regula pentru autoturismele cu utilizare mixtă
                // (aceeași valoare implicită ca în Setări contabilitate), până când contabilul setează alta.
                Apply(entry, VehicleDefault, rule.ValidFrom);
                return;
            }

            Apply(entry, type.Value, setting.ValidFrom);
            entry.DeductibilitySettingId = setting.Id;
            return;
        }

        Apply(entry, rule.DefaultDeductibility, rule.ValidFrom);
    }

    /// <summary>Deductibilitatea auto până la prima setare <c>vehicle_deductibility</c>.</summary>
    public const DeductibilityType VehicleDefault = DeductibilityType.Percent50;

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
        // Doar partea din activitate: partea personală (R30) intră integral la nedeductibil.
        entry.DeductibleAmount = entry.DeductiblePercent is { } percent
            ? LedgerInvariants.Round(entry.BusinessAmount * percent / 100)
            : null;
    }

    private static bool IsValidAt(ExpenseCategoryRule rule, DateOnly date) =>
        rule.ValidFrom <= date && (rule.ValidTo is null || date <= rule.ValidTo);
}
