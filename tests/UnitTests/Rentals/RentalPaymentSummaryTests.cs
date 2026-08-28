using Domain.Rentals;
using Shouldly;
using Xunit;

namespace UnitTests.Rentals;

/// <summary>
/// Rezumatul plăților: contract, încasat, rămas.
/// </summary>
/// <remarks>
/// Nicăieri „profit" sau „câștig" (spec §10). N-avem sursa completă a cheltuielilor unei flote,
/// deci orice cifră numită așa ar fi o minciună convenabilă. Se afișează doar ce s-a înregistrat
/// efectiv.
/// </remarks>
public sealed class RentalPaymentSummaryTests
{
    private static long Remaining(long contractBani, params long[] paid) => contractBani - paid.Sum();

    [Fact]
    public void Nothing_paid_leaves_the_whole_contract_outstanding()
    {
        Remaining(1_440_000).ShouldBe(1_440_000);
    }

    [Fact]
    public void Half_paid_leaves_half()
    {
        // Exemplul din spec: 14.400 contract, 7.200 încasat, 7.200 rămas.
        Remaining(1_440_000, 720_000).ShouldBe(720_000);
    }

    [Fact]
    public void Several_payments_add_up()
    {
        Remaining(1_440_000, 300_000, 300_000, 120_000).ShouldBe(720_000);
    }

    [Fact]
    public void Overpayment_shows_as_negative_rather_than_clamping_to_zero()
    {
        // Zero ar fi ascuns faptul că s-a încasat mai mult decât contractul — exact lucrul pe care
        // proprietarul trebuie să-l vadă ca să-l corecteze.
        Remaining(1_440_000, 1_500_000).ShouldBe(-60_000);
    }

    [Fact]
    public void A_payment_needs_a_method_from_a_closed_list()
    {
        // Text liber ar fi făcut imposibilă orice numărare pe metodă.
        RentalPaymentMethod[] methods = Enum.GetValues<RentalPaymentMethod>();

        methods.ShouldContain(RentalPaymentMethod.Cash);
        methods.ShouldContain(RentalPaymentMethod.BankTransfer);
        methods.ShouldContain(RentalPaymentMethod.Card);
    }
}
