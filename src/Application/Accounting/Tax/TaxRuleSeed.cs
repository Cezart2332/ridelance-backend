using Domain.Accounting;

namespace Application.Accounting.Tax;

/// <summary>
/// Regulile inițiale (spec declarații §2, „datele inițiale de seed”), aceleași ca în migrarea
/// <c>AddTaxRules</c>. Valorile vin din documentul sursă (reverificat de autor la 29.09.2026);
/// cele marcate neconfirmate cer confirmarea contabilului înainte să se aplice singure.
/// </summary>
public static class TaxRuleSeed
{
    public static readonly DateOnly Since = new(2016, 1, 1);

    public static readonly IReadOnlyList<string> EuCountries =
    [
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "EL", "ES", "FI", "FR", "HR", "HU",
        "IE", "IT", "LT", "LU", "LV", "MT", "NL", "PL", "PT", "SE", "SI", "SK",
    ];

    /// <summary>Instanțe noi la fiecare acces: un context EF nu poate urmări aceleași obiecte ca altul.</summary>
    public static IReadOnlyList<TaxRule> Rules => Build();

    private static List<TaxRule> Build()
    {
        int sequence = 0;
        TaxRule Rule(string type, string legalBasis, Action<TaxRule> fill, string jurisdiction = "RO", bool confirmed = true)
        {
            var rule = new TaxRule
            {
                Id = new Guid($"b1c2d3e4-f5a6-4b7c-8d9e-00000007{++sequence:x4}"),
                RuleType = type,
                Jurisdiction = jurisdiction,
                LegalBasis = legalBasis,
                ValidFrom = Since,
                Confirmed = confirmed,
            };
            fill(rule);
            return rule;
        }

        const string Nomenclator = "Nomenclatorul obligațiilor fiscale (OPANAF privind D100)";
        const string Forms = "Instrucțiunile de completare ale formularului (documentul sursă, 29.09.2026)";
        List<TaxRule> rules =
        [
            Rule(TaxRuleTypes.ObligationCode, Nomenclator, r => { r.DeclarationCode = "D100"; r.IncomeType = "COMMISSION"; r.Formula = "634"; }),
            Rule(TaxRuleTypes.ObligationCode, Forms, r => { r.DeclarationCode = "D301"; r.Formula = "301"; }),
            Rule(TaxRuleTypes.BudgetCode, Nomenclator, r => { r.DeclarationCode = "D100"; r.IncomeType = "COMMISSION"; r.Formula = "5503XXXXXX"; }),
            Rule(TaxRuleTypes.Deadline, Forms, r => { r.DeclarationCode = "D100"; r.Formula = "MONTHLY:25"; }),
            Rule(TaxRuleTypes.Deadline, Forms, r => { r.DeclarationCode = "D301"; r.Formula = "MONTHLY:25"; }),
            Rule(TaxRuleTypes.Deadline, Forms, r => { r.DeclarationCode = "D390"; r.Formula = "MONTHLY:25"; }),
            Rule(TaxRuleTypes.Deadline, Forms, r => { r.DeclarationCode = "D207"; r.Formula = "ANNUAL:02-LAST"; }),
            Rule(TaxRuleTypes.Deadline, Forms, r => { r.DeclarationCode = "D212"; r.Formula = "ANNUAL:05-25"; }),
            // Q3: termenul D205 e de confirmat juridic înainte de activarea ramurii.
            Rule(TaxRuleTypes.Deadline, Forms, r => { r.DeclarationCode = "D205"; r.Formula = "ANNUAL:02-LAST"; }, confirmed: false),
            Rule(TaxRuleTypes.Rounding, "XSD-ul ANAF D100 nu acceptă bani pentru sume", r => { r.DeclarationCode = "D100"; r.Formula = nameof(DeclarationRounding.WholeLei); }),
            Rule(TaxRuleTypes.ExchangeRate, "Cursul BNR (Q1, de confirmat)", r => r.Formula = nameof(ExchangeRateDateRule.SameDayOrPrevious), confirmed: false),
            Rule(TaxRuleTypes.Materiality, "Pragul de materialitate pentru payout-uri nereconciliate (Q2)", r => r.Threshold = 1m, confirmed: false),
            // Fallback-ul fără certificat de rezidență: cota din Codul fiscal pentru comisioanele nerezidenților.
            // Neconfirmat: o decizie pe el ajunge la confirmarea Adminului (F23).
            Rule(
                TaxRuleTypes.NonResidentRate,
                "Codul fiscal, Titlul VI (impozitul pe veniturile nerezidenților), cota standard",
                r => { r.IncomeType = "COMMISSION"; r.Rate = 16m; },
                confirmed: false),
            // Q3: reținerea pe chiria de la persoane fizice, de confirmat juridic; până atunci nu produce rânduri.
            Rule(
                TaxRuleTypes.RentWithholding,
                "Codul fiscal, Titlul IV (venituri din cedarea folosinței bunurilor), de confirmat",
                r => { r.IncomeType = "RENT"; r.Rate = 10m; r.Formula = "WITHHOLD_ON_PAYMENT"; },
                confirmed: false),
        ];
        rules.AddRange(EuCountries.Select(country => Rule(TaxRuleTypes.EuMember, "Tratatul de aderare / statele membre UE", _ => { }, country)));
        return rules;
    }
}
