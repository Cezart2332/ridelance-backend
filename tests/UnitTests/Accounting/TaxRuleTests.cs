using Application.Accounting.Tax;
using Domain.Accounting;
using Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>Spec declarații §4 și regula 1: regulile versionate, <c>ResolveRule</c> și validarea configurării.</summary>
public sealed class TaxRuleTests
{
    private static TaxRule Rule(string type, DateOnly from, DateOnly? to = null, string? formula = "X", string? declaration = "D100", string jurisdiction = "RO") => new()
    {
        Id = Guid.NewGuid(), RuleType = type, Jurisdiction = jurisdiction, ValidFrom = from, ValidTo = to,
        LegalBasis = "Test", Formula = formula, DeclarationCode = declaration, Confirmed = true,
    };

    [Fact]
    public void Resolve_ReturnsExactlyTheRuleValidAtTheDate()
    {
        var rules = new TaxRuleSet(
        [
            Rule(TaxRuleTypes.ObligationCode, new DateOnly(2016, 1, 1), new DateOnly(2025, 12, 31), "634"),
            Rule(TaxRuleTypes.ObligationCode, new DateOnly(2026, 1, 1), formula: "699"),
        ]);

        rules.Resolve(TaxRuleTypes.ObligationCode, "RO", new DateOnly(2025, 6, 1), new TaxRuleContext("D100")).Formula.ShouldBe("634");
        rules.Resolve(TaxRuleTypes.ObligationCode, "RO", new DateOnly(2026, 6, 1), new TaxRuleContext("D100")).Formula.ShouldBe("699");
        Should.Throw<TaxRuleConfigurationException>(() => rules.Resolve(TaxRuleTypes.ObligationCode, "RO", new DateOnly(2026, 6, 1), new TaxRuleContext("D301")));
    }

    [Fact]
    public void Resolve_TwoRulesForTheSameDateAreAConfigurationError()
    {
        var rules = new TaxRuleSet([Rule(TaxRuleTypes.Deadline, new DateOnly(2016, 1, 1)), Rule(TaxRuleTypes.Deadline, new DateOnly(2020, 1, 1))]);

        Should.Throw<TaxRuleConfigurationException>(() => rules.Resolve(TaxRuleTypes.Deadline, "RO", new DateOnly(2026, 1, 1), new TaxRuleContext("D100")));
        TaxRuleSet.Validate(rules.Rules).ShouldContain(error => error.Contains("perioade suprapuse", StringComparison.Ordinal));
    }

    /// <summary>Scenariul 15: un document din 2025 recalculat în 2027 primește regula din 2025.</summary>
    [Fact]
    public void S15_ARecalculationUsesTheRuleOfTheDocumentsPeriod()
    {
        var rules = new TaxRuleSet(
        [
            Rule(TaxRuleTypes.Deadline, new DateOnly(2016, 1, 1), new DateOnly(2025, 12, 31), "MONTHLY:25", "D301"),
            Rule(TaxRuleTypes.Deadline, new DateOnly(2026, 1, 1), formula: "MONTHLY:20", declaration: "D301"),
            .. TaxRuleSeed.Rules.Where(rule => rule.RuleType == TaxRuleTypes.EuMember),
        ]);

        TaxEngineSettings.ForPeriod(rules, "2025-06").Declarations![DeclarationType.D301].DueDate.ShouldBe(new DateOnly(2025, 7, 25));
        TaxEngineSettings.ForPeriod(rules, "2027-06").Declarations![DeclarationType.D301].DueDate.ShouldBe(new DateOnly(2027, 7, 20));
    }

    /// <summary>F25 și scenariul 8: o regulă de nerezident legată de brand, fără țară sau bază legală, e refuzată.</summary>
    [Fact]
    public void F25_S8_ANonResidentRuleOnABrandFailsValidation()
    {
        TaxRule brand = Rule(TaxRuleTypes.NonResidentRate, new DateOnly(2026, 1, 1), formula: null, declaration: null, jurisdiction: "");
        brand.SupplierEntityKey = "Bolt";
        brand.LegalBasis = "";
        brand.Rate = 10;
        brand.IncomeType = "COMMISSION";

        IReadOnlyList<string> errors = TaxRuleSet.Validate([brand]);

        errors.ShouldContain(error => error.Contains("nu e un cod fiscal de entitate", StringComparison.Ordinal));
        errors.ShouldContain(error => error.Contains("baza legală", StringComparison.Ordinal));
        errors.ShouldContain(error => error.Contains("jurisdicția", StringComparison.Ordinal));

        TaxRule entity = Rule(TaxRuleTypes.NonResidentRate, new DateOnly(2026, 1, 1), formula: null, declaration: null, jurisdiction: "EE");
        entity.SupplierEntityKey = "EE102090374";
        entity.Rate = 10;
        entity.IncomeType = "COMMISSION";
        TaxRuleSet.Validate([entity]).ShouldBeEmpty();
    }

    /// <summary>F24 și criteriul de acceptanță: seed-ul e valid, iar codul 634 și termenele vin din reguli.</summary>
    [Fact]
    public void F24_TheSeedIsValidAndHoldsTheCodesAndDeadlines()
    {
        TaxRuleSet.Validate(TaxRuleSeed.Rules).ShouldBeEmpty();
        var settings = TaxEngineSettings.ForPeriod(new TaxRuleSet(TaxRuleSeed.Rules), "2026-12");

        settings.Declarations![DeclarationType.D100].ShouldBe(new DeclarationRules("634", "5503XXXXXX", new DateOnly(2027, 1, 25)));
        settings.Declarations[DeclarationType.D301].ObligationCode.ShouldBe("301");
        settings.EuCountries.ShouldContain("EE");
        settings.EuCountries.ShouldNotContain("RO");
        settings.Rounding[DeclarationType.D100].ShouldBe(Application.Accounting.DeclarationRounding.WholeLei);
    }

    [Theory]
    [InlineData("MONTHLY:25", "2026-12", 2027, 1, 25)]
    [InlineData("ANNUAL:02-LAST", "2027", 2028, 2, 29)]
    [InlineData("ANNUAL:02-LAST", "2026", 2027, 2, 28)]
    [InlineData("ANNUAL:05-25", "2026", 2027, 5, 25)]
    public void Deadlines_FollowTheFormulaOfTheRule(string formula, string period, int year, int month, int day) =>
        DeclarationDeadline.Of(formula, period).ShouldBe(new DateOnly(year, month, day));

    /// <summary>Migrarea scrie exact seed-ul din cod (aceleași id-uri, tipuri și valori).</summary>
    [Fact]
    public void Migration_SeedsExactlyTheRulesOfTaxRuleSeed()
    {
        var seeded = new Migration[] { new AddTaxRules(), new AddAnnualDeclarations() }
            .SelectMany(migration => migration.UpOperations.OfType<InsertDataOperation>().Where(op => op.Table == "tax_rules"))
            .SelectMany(insert =>
            {
                int id = Array.IndexOf(insert.Columns, "id");
                int type = Array.IndexOf(insert.Columns, "rule_type");
                int formula = Array.IndexOf(insert.Columns, "formula");
                int jurisdiction = Array.IndexOf(insert.Columns, "jurisdiction");
                return Enumerable.Range(0, insert.Values.GetLength(0))
                    .Select(row => ((Guid)insert.Values[row, id]!, (string)insert.Values[row, type]!, (string)insert.Values[row, jurisdiction]!, insert.Values[row, formula] as string));
            })
            .ToList();
        seeded.ShouldBe([.. TaxRuleSeed.Rules.Select(rule => (rule.Id, rule.RuleType, rule.Jurisdiction, rule.Formula))], ignoreOrder: true);
    }

    /// <summary>Criteriul de acceptanță: niciun <c>if</c> pe brand în motorul fiscal (căutare în cod).</summary>
    [Fact]
    public void NoBrandBranchesInTheTaxEngine()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "CleanArchitecture.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("Rădăcina repo-ului nu a fost găsită.");
        }

        string[] forbidden = ["Platform.Bolt", "Platform.Uber", "\"Bolt\"", "\"Uber\"", "LedgerSource.Bolt", "LedgerSource.Uber"];
        List<string> hits = [.. Directory.EnumerateFiles(Path.Combine(root, "src", "Application", "Accounting", "Tax"), "*.cs")
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(item => !item.line.TrimStart().StartsWith("//", StringComparison.Ordinal) && forbidden.Any(token => item.line.Contains(token, StringComparison.Ordinal)))
            .Select(item => $"{Path.GetFileName(item.file)}:{item.index + 1}")];
        hits.ShouldBeEmpty();
    }
}
