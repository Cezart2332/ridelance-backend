using Domain.PfaRegistrations;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>
/// Venitul lunar al PFA-ului. Regula ușor de greșit e că numerarul și cardul nu sunt venit în
/// plus peste Bolt și Uber, ci aceiași bani văzuți din perspectiva modului de încasare.
/// Adunarea tuturor celor patru câmpuri ar dubla venitul și, prin el, baza impozabilă.
/// </summary>
public sealed class PfaMonthlyIncomeTests
{
    private static PfaMonthlyIncome Income(
        decimal bolt = 0m,
        decimal uber = 0m,
        decimal cash = 0m,
        decimal card = 0m) =>
        new() { VenitBolt = bolt, VenitUber = uber, VenitCash = cash, VenitCard = card };

    [Fact]
    public void Platform_and_payment_views_describe_the_same_money()
    {
        // 3000 din platforme, aceiași 3000 împărțiți în numerar și card.
        PfaMonthlyIncome income = Income(bolt: 2_000m, uber: 1_000m, cash: 1_200m, card: 1_800m);

        income.ComputeVenitTotal().ShouldBe(3_000m);
    }

    [Fact]
    public void Months_without_platform_data_fall_back_to_the_hand_typed_split()
    {
        PfaMonthlyIncome income = Income(cash: 900m, card: 1_100m);

        income.ComputeVenitTotal().ShouldBe(2_000m);
    }

    [Fact]
    public void Months_without_a_payment_split_use_the_platform_totals()
    {
        PfaMonthlyIncome income = Income(bolt: 2_500m, uber: 500m);

        income.ComputeVenitTotal().ShouldBe(3_000m);
    }

    [Fact]
    public void The_larger_view_wins_when_the_two_disagree()
    {
        // Un import parțial poate lăsa o latură mai mică; nu se pierde venit raportat.
        Income(bolt: 3_000m, cash: 500m).ComputeVenitTotal().ShouldBe(3_000m);
        Income(bolt: 500m, cash: 3_000m).ComputeVenitTotal().ShouldBe(3_000m);
    }

    [Fact]
    public void Tax_base_counts_only_platform_income()
    {
        // Baza fiscală nu se uită la split-ul de plată, oricât ar fi el.
        PfaMonthlyIncome income = Income(bolt: 2_000m, uber: 1_000m, cash: 5_000m, card: 5_000m);

        income.ComputePlatformIncome().ShouldBe(3_000m);
    }

    [Fact]
    public void An_empty_month_is_zero_not_a_guess()
    {
        Income().ComputeVenitTotal().ShouldBe(0m);
        Income().ComputePlatformIncome().ShouldBe(0m);
    }
}
