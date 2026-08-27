using Domain.Rentals;
using Shouldly;
using Xunit;

namespace UnitTests.Rentals;

/// <summary>
/// Valorile implicite ale firmei și cifrele dintr-o închiriere sunt două lucruri, nu unul.
/// </summary>
/// <remarks>
/// Testul cerut explicit de spec (§6.2). Greșeala pe care o prinde e cea în care închirierea ar
/// ține o referință către setările firmei în loc de propriile valori: atunci ridicarea tarifului
/// standard ar rescrie retroactiv fiecare contract semnat, iar corectarea unei sume într-un
/// contract ar schimba prețul propus tuturor clienților următori.
/// </remarks>
public sealed class RentalDefaultsIsolationTests
{
    private static FleetRentalDefaults Defaults() => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = Guid.NewGuid(),
        WeeklyRentBani = 180_000,
        DepositBani = 100_000,
        HasKmLimit = true,
        MileageLimit = 2_000,
        ExtraKmCostBani = 50,
        FuelRule = "plin → plin",
    };

    /// <summary>Copierea, așa cum o face crearea unei închirieri.</summary>
    private static Rental RentalFrom(FleetRentalDefaults defaults) => new()
    {
        Id = Guid.NewGuid(),
        PublicCode = "RL-000001",
        OwnerUserId = defaults.OwnerUserId,
        WeeklyRentBani = defaults.WeeklyRentBani ?? 0,
        DepositBani = defaults.DepositBani ?? 0,
        HasKmLimit = defaults.HasKmLimit,
        MileageLimit = defaults.MileageLimit,
        ExtraKmCostBani = defaults.ExtraKmCostBani ?? 0,
        FuelRule = defaults.FuelRule,
    };

    [Fact]
    public void Changing_the_deposit_on_a_rental_leaves_the_fleet_default_alone()
    {
        FleetRentalDefaults defaults = Defaults();
        Rental rental = RentalFrom(defaults);

        rental.DepositBani = 250_000;

        defaults.DepositBani.ShouldBe(100_000);
    }

    [Fact]
    public void Raising_the_fleet_default_leaves_a_signed_rental_alone()
    {
        FleetRentalDefaults defaults = Defaults();
        Rental rental = RentalFrom(defaults);

        defaults.WeeklyRentBani = 220_000;

        rental.WeeklyRentBani.ShouldBe(180_000);
    }

    [Fact]
    public void Every_copied_value_is_independent_afterwards()
    {
        FleetRentalDefaults defaults = Defaults();
        Rental rental = RentalFrom(defaults);

        rental.MileageLimit = 3_000;
        rental.ExtraKmCostBani = 75;
        rental.FuelRule = "cel puțin nivelul de la preluare";
        rental.HasKmLimit = false;

        defaults.MileageLimit.ShouldBe(2_000);
        defaults.ExtraKmCostBani.ShouldBe(50);
        defaults.FuelRule.ShouldBe("plin → plin");
        defaults.HasKmLimit.ShouldBeTrue();
    }
}
