using System.Globalization;
using System.Text.RegularExpressions;
using Application.Abstractions.Data;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace Application.Accounting.Tax;

/// <summary>Contextul în care se caută o regulă: declarația, furnizorul juridic, tipul venitului.</summary>
public sealed record TaxRuleContext(string? DeclarationCode = null, string? SupplierEntityKey = null, string? IncomeType = null);

/// <summary>
/// Configurare greșită: zero sau mai multe reguli pentru aceeași căutare. Nu se tratează ca un caz
/// normal; se repară configurarea (spec declarații §4).
/// </summary>
public sealed class TaxRuleConfigurationException(string message) : InvalidOperationException(message);

/// <summary>
/// Regulile fiscale versionate (spec declarații §4, regula 1): <see cref="Resolve"/> întoarce exact
/// regula valabilă la dată pentru tip, jurisdicție și context. Un document din 2025 recalculat în
/// 2027 primește regula din 2025.
/// </summary>
public sealed partial class TaxRuleSet(IReadOnlyList<TaxRule> rules)
{
    public IReadOnlyList<TaxRule> Rules { get; } = rules;

    public static async Task<TaxRuleSet> LoadAsync(IApplicationDbContext db, CancellationToken cancellationToken) =>
        new(await db.TaxRules.AsNoTracking().ToListAsync(cancellationToken));

    /// <summary>Exact o regulă; zero sau mai multe aruncă <see cref="TaxRuleConfigurationException"/>.</summary>
    public TaxRule Resolve(string type, string jurisdiction, DateOnly date, TaxRuleContext? context = null) =>
        Find(type, jurisdiction, date, context) ??
        throw new TaxRuleConfigurationException(
            $"Nicio regulă {type} pentru {jurisdiction}{Describe(context)} la {date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}.");

    /// <summary>Regula valabilă sau <c>null</c> dacă nu există; mai multe aruncă (configurare greșită).</summary>
    public TaxRule? Find(string type, string jurisdiction, DateOnly date, TaxRuleContext? context = null)
    {
        context ??= new TaxRuleContext();
        List<TaxRule> matches = [.. Rules.Where(rule =>
            rule.RuleType == type &&
            Same(rule.Jurisdiction, jurisdiction) &&
            Same(rule.DeclarationCode, context.DeclarationCode) &&
            Same(rule.SupplierEntityKey, context.SupplierEntityKey) &&
            Same(rule.IncomeType, context.IncomeType) &&
            rule.ValidFrom <= date && (rule.ValidTo is null || date <= rule.ValidTo))];
        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new TaxRuleConfigurationException(
                $"{matches.Count} reguli {type} pentru {jurisdiction}{Describe(context)} la {date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}."),
        };
    }

    /// <summary>Statul e membru UE la dată.</summary>
    public bool IsEuMember(string country, DateOnly date) => Find(TaxRuleTypes.EuMember, country, date) is not null;

    /// <summary>Ce identifică regulile active: id-urile și datele lor, ca versiune a setului folosit la un calcul.</summary>
    public string VersionAt(DateOnly date)
    {
        string active = string.Join(';', Rules
            .Where(rule => rule.ValidFrom <= date && (rule.ValidTo is null || date <= rule.ValidTo))
            .OrderBy(rule => rule.Id)
            .Select(rule => $"{rule.Id:N}@{rule.ValidFrom:yyyyMMdd}"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(active)))[..12];
    }

    /// <summary>
    /// Validarea configurării (spec declarații F25, scenariul 8): câmpurile obligatorii pe tip, regulile
    /// pe furnizor legate de un cod fiscal (nu de brand), cu țară și bază legală, fără suprapuneri.
    /// Listă goală = configurare corectă.
    /// </summary>
    public static IReadOnlyList<string> Validate(IEnumerable<TaxRule> rules)
    {
        List<TaxRule> list = [.. rules];
        List<string> errors = [];
        foreach (TaxRule rule in list)
        {
            string name = $"{rule.RuleType} {rule.Jurisdiction}{Describe(new TaxRuleContext(rule.DeclarationCode, rule.SupplierEntityKey, rule.IncomeType))}";
            if (rule.ValidTo < rule.ValidFrom)
            {
                errors.Add($"{name}: ValidTo e înainte de ValidFrom.");
            }

            if (string.IsNullOrWhiteSpace(rule.Jurisdiction) || rule.Jurisdiction.Length != 2)
            {
                errors.Add($"{name}: jurisdicția e codul de țară din două litere.");
            }

            if (string.IsNullOrWhiteSpace(rule.LegalBasis))
            {
                errors.Add($"{name}: lipsește baza legală.");
            }

            errors.AddRange(Required(rule, name));
            if (rule.SupplierEntityKey is { } key && !TaxIdPattern().IsMatch(key))
            {
                errors.Add($"{name}: „{key}” nu e un cod fiscal de entitate; regulile se leagă de furnizorul juridic, nu de brand.");
            }
        }

        foreach (IGrouping<string, TaxRule> group in list.GroupBy(rule =>
                     $"{rule.RuleType}|{rule.Jurisdiction}|{rule.DeclarationCode}|{rule.SupplierEntityKey}|{rule.IncomeType}".ToUpperInvariant()))
        {
            List<TaxRule> ordered = [.. group.OrderBy(rule => rule.ValidFrom)];
            for (int index = 1; index < ordered.Count; index++)
            {
                if (ordered[index - 1].ValidTo is not { } previousEnd || previousEnd >= ordered[index].ValidFrom)
                {
                    errors.Add($"{ordered[index].RuleType} {ordered[index].Jurisdiction}: perioade suprapuse de la {ordered[index].ValidFrom:dd.MM.yyyy}.");
                }
            }
        }

        return errors;
    }

    private static IEnumerable<string> Required(TaxRule rule, string name)
    {
        bool needsDeclaration = rule.RuleType is TaxRuleTypes.ObligationCode or TaxRuleTypes.BudgetCode or TaxRuleTypes.Deadline or TaxRuleTypes.Rounding;
        if (needsDeclaration && string.IsNullOrWhiteSpace(rule.DeclarationCode))
        {
            yield return $"{name}: lipsește declarația.";
        }

        bool needsFormula = needsDeclaration || rule.RuleType == TaxRuleTypes.ExchangeRate;
        if (needsFormula && string.IsNullOrWhiteSpace(rule.Formula))
        {
            yield return $"{name}: lipsește valoarea (Formula).";
        }

        if (rule.RuleType is TaxRuleTypes.NonResidentRate or TaxRuleTypes.RentWithholding)
        {
            if (rule.Rate is not { } rate || rate < 0 || rate > 100)
            {
                yield return $"{name}: cota lipsește sau nu e între 0 și 100.";
            }

            if (string.IsNullOrWhiteSpace(rule.IncomeType))
            {
                yield return $"{name}: lipsește tipul venitului.";
            }
        }

        if (rule.RuleType == TaxRuleTypes.Materiality && rule.Threshold is null)
        {
            yield return $"{name}: lipsește pragul.";
        }
    }

    /// <summary>Un cod fiscal de entitate juridică (cheia unei reguli pe furnizor), nu un nume de brand.</summary>
    public static bool IsEntityKey(string? key) => key is not null && TaxIdPattern().IsMatch(key);

    private static bool Same(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string Describe(TaxRuleContext? context)
    {
        if (context is null)
        {
            return string.Empty;
        }

        var text = new System.Text.StringBuilder();
        if (context.DeclarationCode is { } declaration)
        {
            text.Append(", ").Append(declaration);
        }

        if (context.SupplierEntityKey is { } supplier)
        {
            text.Append(", furnizor ").Append(supplier);
        }

        if (context.IncomeType is { } income)
        {
            text.Append(", ").Append(income);
        }

        return text.ToString();
    }

    /// <summary>Cod fiscal: prefixul de țară și cel puțin o cifră (<c>EE102090374</c>, <c>NL852071589B01</c>).</summary>
    [GeneratedRegex("^[A-Z]{2}(?=[A-Z0-9]*[0-9])[A-Z0-9]{2,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex TaxIdPattern();
}
