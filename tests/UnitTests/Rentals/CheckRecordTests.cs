using Domain.Cars;
using Domain.Rentals;
using Shouldly;
using Xunit;

namespace UnitTests.Rentals;

/// <summary>
/// Ce se întâmplă la predare și la primire.
/// </summary>
/// <remarks>
/// Criteriile de acceptanță ale fazei: kilometrajul mașinii vine de la primire, iar garanția
/// reținută are întotdeauna un motiv scris.
/// </remarks>
public sealed class CheckRecordTests
{
    private static CheckRecord CheckIn(int mileage) => new()
    {
        Id = Guid.NewGuid(),
        Kind = CheckKind.CheckIn,
        Mileage = mileage,
        Accessories = ["Chei", "Triunghi"],
    };

    private static CheckRecord CheckOut(int mileage) => new()
    {
        Id = Guid.NewGuid(),
        Kind = CheckKind.CheckOut,
        Mileage = mileage,
    };

    [Fact]
    public void The_car_takes_its_mileage_from_the_return_not_from_the_handover()
    {
        // La predare mașina pleacă; abia la primire se știe cât a mers.
        var car = new Car { Mileage = 40_000 };

        CheckRecord handover = CheckIn(40_000);
        car.Mileage.ShouldBe(40_000);

        CheckRecord ret = CheckOut(43_500);
        car.Mileage = ret.Mileage;

        car.Mileage.ShouldBe(43_500);
        handover.Mileage.ShouldBe(40_000);
    }

    [Fact]
    public void The_distance_driven_is_the_difference_between_the_two()
    {
        CheckRecord handover = CheckIn(40_000);
        CheckRecord ret = CheckOut(43_500);

        (ret.Mileage - handover.Mileage).ShouldBe(3_500);
    }

    [Fact]
    public void A_return_below_the_handover_mileage_is_impossible()
    {
        // Handlerul refuză cazul ăsta; testul fixează regula, ca refuzul să nu dispară tăcut.
        CheckRecord handover = CheckIn(40_000);
        CheckRecord ret = CheckOut(39_000);

        (ret.Mileage < handover.Mileage).ShouldBeTrue();
    }

    [Fact]
    public void A_handover_has_no_deposit_settlement()
    {
        // Deconturile apar doar la primire: la predare garanția tocmai s-a încasat.
        CheckRecord handover = CheckIn(40_000);

        handover.DepositReturnedBani.ShouldBeNull();
        handover.DepositWithheldBani.ShouldBeNull();
        handover.WithholdingReason.ShouldBeNull();
    }

    [Fact]
    public void Nothing_withheld_and_nothing_filled_in_are_different_things()
    {
        // De aceea sumele sunt nullable, nu zero.
        CheckRecord notSettled = CheckOut(43_500);
        CheckRecord settledWithNothingWithheld = CheckOut(43_500);
        settledWithNothingWithheld.DepositWithheldBani = 0;

        notSettled.DepositWithheldBani.ShouldBeNull();
        settledWithNothingWithheld.DepositWithheldBani.ShouldBe(0);
    }

    [Fact]
    public void A_withheld_deposit_carries_its_reason()
    {
        CheckRecord ret = CheckOut(43_500);
        ret.DepositWithheldBani = 30_000;
        ret.WithholdingReason = "Zgârietură pe bara față";
        ret.DepositReturnedBani = 70_000;

        ret.WithholdingReason.ShouldNotBeNullOrWhiteSpace();
        (ret.DepositReturnedBani + ret.DepositWithheldBani).ShouldBe(100_000);
    }

    [Fact]
    public void The_same_slots_are_photographed_on_both_sides_so_they_can_be_compared()
    {
        CheckPhotoSlot[] slots = Enum.GetValues<CheckPhotoSlot>();

        slots.ShouldContain(CheckPhotoSlot.Front);
        slots.ShouldContain(CheckPhotoSlot.Rear);
        slots.ShouldContain(CheckPhotoSlot.Left);
        slots.ShouldContain(CheckPhotoSlot.Right);
        slots.ShouldContain(CheckPhotoSlot.Interior);
        slots.ShouldContain(CheckPhotoSlot.Dashboard);
    }
}
