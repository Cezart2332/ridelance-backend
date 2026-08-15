using Application.PfaDashboard;
using Application.PfaRegistrations;
using Shouldly;
using Xunit;

namespace UnitTests.PfaDashboard;

/// <summary>
/// „Cât trebuie să pui deoparte" — cifra care apare și pe Acasă, și în Situație financiară,
/// și care alimentează profitul real estimat. Până acum nu avea niciun test, deși e valoarea
/// pe care utilizatorul o folosește ca să-și pună bani deoparte.
///
/// Testele fixează cotele explicit: valorile implicite se pot schimba din configurare, iar un
/// test care depinde de ele ar începe să pice fără ca vreo regulă să se fi rupt.
/// </summary>
public sealed class TaxReserveCalculatorTests
{
    private static readonly FiscalPolicyOptions Fiscal = new()
    {
        VatIntracomRate = 0.21m,
        BoltNonResidentRate = 0.02m,
    };

    private static readonly PfaTaxCalculator.TaxResult AnnualTaxes = new(
        Profit: 120_000m,
        Cas: 9_720m,
        Cass: 4_860m,
        IncomeTax: 12_000m,
        TotalTax: 26_580m,
        NetIncome: 93_420m);

    private static TaxReserveResult Compute(
        decimal platformFees = 1_000m,
        decimal boltFees = 600m,
        decimal periodNet = 10_000m,
        decimal annualIncome = 120_000m) =>
        TaxReserveCalculator.Compute(platformFees, boltFees, periodNet, annualIncome, AnnualTaxes, Fiscal);

    private static decimal AmountOf(TaxReserveResult result, string key) =>
        result.Components.Single(c => c.Key == key).Amount;

    [Fact]
    public void Reserve_has_the_four_components_the_spec_names()
    {
        TaxReserveResult result = Compute();

        result.Components.Select(c => c.Key)
            .ShouldBe(["vatIntracom", "boltNonResident", "incomeTax", "casCass"]);
    }

    [Fact]
    public void Vat_is_a_rate_on_the_whole_platform_commission()
    {
        TaxReserveResult result = Compute(platformFees: 1_000m);

        AmountOf(result, "vatIntracom").ShouldBe(210m);
    }

    [Fact]
    public void Non_resident_tax_applies_only_to_the_Bolt_commission()
    {
        // Comisionul total e 1000, dar taxa de nerezident privește doar partea Bolt.
        TaxReserveResult result = Compute(platformFees: 1_000m, boltFees: 600m);

        AmountOf(result, "boltNonResident").ShouldBe(12m);
    }

    [Fact]
    public void Annual_taxes_are_allocated_by_the_period_share_of_annual_income()
    {
        // 10.000 din 120.000 = a douăsprezecea parte din an.
        TaxReserveResult result = Compute(periodNet: 10_000m, annualIncome: 120_000m);

        AmountOf(result, "incomeTax").ShouldBe(1_000m);
        AmountOf(result, "casCass").ShouldBe(1_215m);
    }

    [Fact]
    public void Period_share_is_capped_at_one_hundred_percent()
    {
        // O lună excepțională nu poate cere mai mult decât impozitul întregului an.
        TaxReserveResult result = Compute(periodNet: 500_000m, annualIncome: 120_000m);

        AmountOf(result, "incomeTax").ShouldBe(AnnualTaxes.IncomeTax);
        AmountOf(result, "casCass").ShouldBe(AnnualTaxes.Cas + AnnualTaxes.Cass);
    }

    [Fact]
    public void Without_annual_income_nothing_is_allocated_from_the_annual_taxes()
    {
        // Primul an, fără venit anual încă: nu se împarte la zero și nu se inventează o cotă.
        TaxReserveResult result = Compute(annualIncome: 0m);

        AmountOf(result, "incomeTax").ShouldBe(0m);
        AmountOf(result, "casCass").ShouldBe(0m);
    }

    [Fact]
    public void Total_is_exactly_the_sum_of_the_components()
    {
        TaxReserveResult result = Compute();

        result.Total.ShouldBe(result.Components.Sum(c => c.Amount));
    }

    [Fact]
    public void Each_component_carries_the_basis_it_was_computed_from()
    {
        // Fără bază afișată, „estimare" devine o cifră pe care userul nu o poate verifica.
        TaxReserveResult result = Compute(platformFees: 1_000m, boltFees: 600m);

        result.Components.Single(c => c.Key == "vatIntracom").Basis.ShouldBe(1_000m);
        result.Components.Single(c => c.Key == "boltNonResident").Basis.ShouldBe(600m);
        result.Components.Single(c => c.Key == "vatIntracom").Rate.ShouldBe(0.21m);
        result.Components.Single(c => c.Key == "boltNonResident").Rate.ShouldBe(0.02m);
    }

    [Fact]
    public void Amounts_are_rounded_to_two_decimals_away_from_zero()
    {
        // 0.21 * 333.33 = 69.9993 → 70.00, nu 69.99.
        TaxReserveResult result = Compute(platformFees: 333.33m);

        AmountOf(result, "vatIntracom").ShouldBe(70m);
    }

    [Fact]
    public void Real_profit_subtracts_expenses_and_the_reserve_from_net_earnings()
    {
        TaxReserveCalculator.RealProfit(10_000m, 1_500m, 2_000m).ShouldBe(6_500m);
    }

    [Fact]
    public void Real_profit_can_go_negative_when_costs_exceed_earnings()
    {
        // Nu se plafonează la zero: o lună slabă trebuie să se vadă ca atare.
        TaxReserveCalculator.RealProfit(1_000m, 900m, 500m).ShouldBe(-400m);
    }
}
